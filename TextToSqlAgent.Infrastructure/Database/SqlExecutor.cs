using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.ErrorHandling;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.Database;

public class SqlExecutor
{
    private readonly DatabaseConfig _config;
    private readonly ILogger<SqlExecutor> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ConnectionErrorHandler? _connectionErrorHandler;
    private readonly SqlErrorHandler? _sqlErrorHandler;

    public SqlExecutor(
        DatabaseConfig config, 
        ILogger<SqlExecutor> logger,
        ConnectionErrorHandler? connectionErrorHandler = null,
        SqlErrorHandler? sqlErrorHandler = null)
    {
        _config = config;
        _logger = logger;
        _connectionErrorHandler = connectionErrorHandler;
        _sqlErrorHandler = sqlErrorHandler;

        // Setup retry policy with Polly (fallback if handlers not provided)
        _retryPolicy = Policy
            .Handle<SqlException>(ex => IsTransient(ex))
            .WaitAndRetryAsync(
                retryCount: config.MaxRetryAttempts,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "[Database] Retry {RetryCount}/{MaxRetries} after {Delay}s due to error: {Error}",
                        retryCount,
                        config.MaxRetryAttempts,
                        timeSpan.TotalSeconds,
                        exception.Message);
                });
    }

    public async Task<SqlExecutionResult> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Database] Executing SQL...");
        _logger.LogDebug("[Database] SQL: {SQL}", sql);

        try
        {
            // Execute query directly - no pre-check
            return await ExecuteQueryInternalAsync(sql, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "[Database] SQL execution failed, attempting error recovery...");

            // Try error recovery if handler available
            if (_connectionErrorHandler != null)
            {
                try
                {
                    return await _connectionErrorHandler.HandleConnectionErrorAsync(
                        async () => await ExecuteQueryInternalAsync(sql, cancellationToken),
                        ex,
                        cancellationToken);
                }
                catch
                {
                    // If recovery fails, proceed to error analysis
                }
            }

            _logger.LogError(ex, "[Database] SQL Error: {Message}", ex.Message);

            // Use SQL error handler if available
            if (_sqlErrorHandler != null)
            {
                var sqlError = _sqlErrorHandler.AnalyzeError(ex.Message, sql);
                
                return new SqlExecutionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ErrorDetails = new Dictionary<string, object>
                    {
                        ["ErrorType"] = sqlError.Type.ToString(),
                        ["ErrorCode"] = sqlError.ErrorCode ?? "SQL_ERR",
                        ["Severity"] = sqlError.Severity.ToString(),
                        ["IsRecoverable"] = sqlError.IsRecoverable,
                        ["SuggestedFix"] = sqlError.SuggestedFix ?? "",
                        ["InvalidElement"] = sqlError.InvalidElement ?? ""
                    }
                };
            }

            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Database] Unexpected error executing SQL");

            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private async Task<SqlExecutionResult> ExecuteQueryInternalAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(_config.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _config.CommandTimeout,
            CommandType = CommandType.Text
        };

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new SqlExecutionResult
        {
            Success = true,
            Columns = new List<string>(),
            Rows = new List<Dictionary<string, object>>()
        };

        // Get column names
        for (int i = 0; i < reader.FieldCount; i++)
        {
            result.Columns.Add(reader.GetName(i));
        }

        // Read rows
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[columnName] = value ?? DBNull.Value;
            }

            result.Rows.Add(row);
        }

        _logger.LogInformation(
            "[Database] Query completed: {RowCount} rows, {ColumnCount} columns",
            result.RowCount,
            result.Columns.Count);

        return result;
    }

    private static bool IsTransient(SqlException ex)
    {
        // SQL Server transient error codes
        var transientErrors = new[]
        {
            -1,    // Timeout
            -2,    // Connection broken
            1205,  // Deadlock
            4060,  // Cannot open database
            40197, // Service error
            40501, // Service busy
            40613, // Database unavailable
            49918, // Cannot process request
            49919, // Too many create/update operations
            49920  // Too many operations
        };

        return transientErrors.Contains(ex.Number);
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Validate connection string first
        if (string.IsNullOrWhiteSpace(_config.ConnectionString))
        {
            _logger.LogError("[Database] Connection string is null or empty");
            return false;
        }

        try
        {
            using var connection = new SqlConnection(_config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            _logger.LogInformation("[Database] Connection validated successfully");
            return true;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "[Database] SQL connection failed: {Message}", ex.Message);
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "[Database] Invalid connection string format: {Message}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Database] Connection validation failed: {Message}", ex.Message);
            return false;
        }
    }
}