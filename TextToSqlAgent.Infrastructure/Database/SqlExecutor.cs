using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.Database;

/// <summary>
/// SQL query executor that uses adapters for database-agnostic execution
/// </summary>
public class SqlExecutor
{
    private readonly DatabaseConfig _config;
    private readonly IDatabaseAdapter _adapter;
    private readonly ILogger<SqlExecutor> _logger;

    public SqlExecutor(
        DatabaseConfig config,
        IDatabaseAdapter adapter,
        ILogger<SqlExecutor> logger)
    {
        _config = config;
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<SqlExecutionResult> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlExecutor] Executing SQL on {Provider}...", _adapter.Provider);

        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;

        while (attempt < _config.MaxRetryAttempts)
        {
            attempt++;

            try
            {
                using var connection = _adapter.CreateConnection(_config.ConnectionString);
                
                // Cast to DbConnection for OpenAsync support
                if (connection is DbConnection dbConnection)
                {
                    await dbConnection.OpenAsync(cancellationToken);
                }
                else
                {
                    connection.Open();
                }

                var result = await ExecuteQueryAsync(connection, sql, cancellationToken);
                stopwatch.Stop();

                result.ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds;

                _logger.LogInformation(
                    "[SqlExecutor] Query executed successfully on {Provider} ({Rows} rows, {Time}ms)",
                    _adapter.Provider,
                    result.Rows.Count,
                    result.ExecutionTimeMs);

                return result;
            }
            catch (Exception ex)
            {
                if (_adapter.IsTransientError(ex) && attempt < _config.MaxRetryAttempts)
                {
                    _logger.LogWarning(
                        ex,
                        "[SqlExecutor] Transient error on {Provider}, retrying... (Attempt {Attempt}/{Max})",
                        _adapter.Provider,
                        attempt,
                        _config.MaxRetryAttempts);

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                    continue;
                }

                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "[SqlExecutor] Query failed on {Provider} after {Attempts} attempts: {Message}",
                    _adapter.Provider,
                    attempt,
                    ex.Message);

                return new SqlExecutionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Rows = new List<Dictionary<string, object?>>()
                };
            }
        }

        stopwatch.Stop();
        return new SqlExecutionResult
        {
            Success = false,
            ErrorMessage = $"Max retry attempts ({_config.MaxRetryAttempts}) reached",
            ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
            Rows = new List<Dictionary<string, object?>>()
        };
    }

    private async Task<SqlExecutionResult> ExecuteQueryAsync(
        IDbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(
            sql,
            commandTimeout: _config.CommandTimeout,
            cancellationToken: cancellationToken);

        var reader = await connection.QueryAsync(command);

        var rows = new List<Dictionary<string, object?>>();

        foreach (var row in reader)
        {
            var dict = new Dictionary<string, object?>();
            var dapperRow = row as IDictionary<string, object>;

            if (dapperRow != null)
            {
                foreach (var kvp in dapperRow)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }

            rows.Add(dict);
        }

        return new SqlExecutionResult
        {
            Success = true,
            Rows = rows,
            RowsAffected = rows.Count
        };
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ConnectionString))
        {
            _logger.LogError("[SqlExecutor] Connection string is null or empty");
            return false;
        }

        try
        {
            var success = await _adapter.TestConnectionAsync(_config.ConnectionString, cancellationToken);
            
            if (success)
            {
                _logger.LogInformation("[SqlExecutor] Connection validated successfully for {Provider}", _adapter.Provider);
            }
            else
            {
                _logger.LogError("[SqlExecutor] Connection validation failed for {Provider}", _adapter.Provider);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SqlExecutor] Connection validation failed for {Provider}: {Message}", 
                _adapter.Provider, ex.Message);
            return false;
        }
    }
}