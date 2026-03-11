using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Database;

namespace TextToSqlAgent.Infrastructure.Tools;

/// <summary>
/// Tool for executing SQL queries
/// </summary>
public class SqlExecutorTool : ITool
{
    private readonly SqlExecutor _sqlExecutor;
    private readonly ILogger<SqlExecutorTool> _logger;

    public string Name => "execute_sql";

    public string Description => @"Execute a SQL query and return results.
Use this tool after generating SQL to actually run it against the database.
Input: sql (string) - the SQL query to execute
Output: Query execution result with rows and metadata";

    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new()
            {
                Name = "sql",
                Type = "string",
                Description = "The SQL query to execute",
                Required = true
            }
        }
    };

    public SqlExecutorTool(
        SqlExecutor sqlExecutor,
        ILogger<SqlExecutorTool> logger)
    {
        _sqlExecutor = sqlExecutor;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var sql = input.GetString("sql");
            _logger.LogInformation("[SqlExecutorTool] Executing SQL: {SQL}", sql);

            var result = await _sqlExecutor.ExecuteAsync(sql, ct);

            if (result.Success)
            {
                _logger.LogInformation("[SqlExecutorTool] Query successful, {Rows} rows returned",
                    result.RowCount);
                return ToolResult.FromSuccess(result);
            }
            else
            {
                _logger.LogWarning("[SqlExecutorTool] Query failed: {Error}", result.ErrorMessage);
                return ToolResult.FromError(result.ErrorMessage ?? "Query execution failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SqlExecutorTool] Error executing SQL");
            return ToolResult.FromError($"Failed to execute SQL: {ex.Message}");
        }
    }
}
