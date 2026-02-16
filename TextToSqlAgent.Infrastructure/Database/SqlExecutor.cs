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
        // FIX: Validate SQL before execution
        if (string.IsNullOrWhiteSpace(sql))
        {
            _logger.LogError("[SqlExecutor] SQL is null or empty");
            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = "SQL query cannot be empty",
                Rows = new List<Dictionary<string, object?>>()
            };
        }

        _logger.LogDebug("[SqlExecutor] Executing SQL on {Provider}...", _adapter.Provider);

        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;

        while (attempt < _config.MaxRetryAttempts)
        {
            attempt++;

            DbConnection? connection = null;
            try
            {
                // FIX: Create connection with proper using statement
                connection = _adapter.CreateConnection(_config.ConnectionString);
                
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // FIX: Handle timeout specifically
                stopwatch.Stop();
                _logger.LogWarning(
                    "[SqlExecutor] Query timeout on {Provider} after {Time}ms",
                    _adapter.Provider,
                    stopwatch.ElapsedMilliseconds);

                return new SqlExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Query execution timed out after {stopwatch.ElapsedMilliseconds}ms",
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Rows = new List<Dictionary<string, object?>>()
                };
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

                // FIX: Return detailed error information
                return new SqlExecutionResult
                {
                    Success = false,
                    ErrorMessage = FormatErrorMessage(ex),
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Rows = new List<Dictionary<string, object?>>()
                };
            }
            finally
            {
                // FIX: Ensure connection is always closed
                if (connection != null)
                {
                    try
                    {
                        if (connection.State != ConnectionState.Closed)
                        {
                            connection.Close();
                        }
                        connection.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[SqlExecutor] Error closing connection");
                    }
                }
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
        var commandTimeout = _config.CommandTimeout > 0 ? _config.CommandTimeout : 30;
        
        var command = new CommandDefinition(
            sql,
            commandTimeout: commandTimeout,
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
                    // FIX: Handle null values properly
                    dict[kvp.Key] = kvp.Value == DBNull.Value ? null : kvp.Value;
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

        DbConnection? connection = null;
        try
        {
            connection = _adapter.CreateConnection(_config.ConnectionString);
            
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
            
            _logger.LogInformation("[SqlExecutor] Connection validated successfully for {Provider}", _adapter.Provider);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SqlExecutor] Connection validation failed for {Provider}: {Message}", 
                _adapter.Provider, ex.Message);
            return false;
        }
        finally
        {
            // FIX: Ensure connection is always closed
            if (connection != null)
            {
                try
                {
                    if (connection.State != ConnectionState.Closed)
                    {
                        connection.Close();
                    }
                    connection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SqlExecutor] Error closing connection");
                }
            }
        }
    }

    /// <summary>
    /// Format error message to be user-friendly
    /// </summary>
    private string FormatErrorMessage(Exception ex)
    {
        var message = ex.Message;

        // Make error messages more user-friendly
        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Query execution timed out. Please try again or simplify your query.";
        }

        if (message.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot connect to database. Please check your connection settings.";
        }

        if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("access", StringComparison.OrdinalIgnoreCase))
        {
            return "Insufficient database permissions. Please contact your database administrator.";
        }

        if (message.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("SQL", StringComparison.OrdinalIgnoreCase))
        {
            return $"SQL Error: {message}";
        }

        return $"Error: {message}";
    }
}
