using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;

namespace TextToSqlAgent.Application.Agent.Tools;

/// <summary>
/// Tool that executes a SQL query against the connected database.
/// Uses SqlExecutor via IAgentServiceFactory for safe query execution.
/// </summary>
public class SqlExecutionTool : IAgentTool
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly ILogger<SqlExecutionTool> _logger;

    public string Name => "SqlExecution";
    public string Description =>
        "Execute a SQL query against the database and return results. " +
        "Only use this after generating SQL with SqlGeneration. " +
        "Returns the query results (rows, columns) or an error message.";

    public SqlExecutionTool(IAgentServiceFactory serviceFactory, ILogger<SqlExecutionTool> logger)
    {
        _serviceFactory = serviceFactory;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, WorkingMemory memory, CancellationToken ct)
    {
        try
        {
            var sql = memory.GeneratedSql ?? input.Query;

            if (string.IsNullOrEmpty(sql))
            {
                return ToolResult.Fail("No SQL query to execute. Use SqlGeneration first.");
            }

            // Safety: only allow SELECT queries
            if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResult.Fail("Only SELECT queries are allowed for safety.");
            }

            var executor = _serviceFactory.GetSqlExecutor();
            var result = await executor.ExecuteAsync(sql, ct);

            memory.ExecutionResult = result;

            if (result.Success)
            {
                _logger.LogInformation("[SqlExecutionTool] Query returned {Rows} rows", result.RowCount);

                var summary = $"Query executed successfully. Returned {result.RowCount} rows.";
                if (result.Rows?.Count > 0)
                {
                    var preview = result.Rows.Take(3)
                        .Select(r => string.Join(", ", r.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
                    summary += $"\nPreview:\n{string.Join("\n", preview)}";
                }

                return ToolResult.Ok(summary, result);
            }
            else
            {
                var error = result.ErrorMessage ?? "Unknown execution error";
                _logger.LogWarning("[SqlExecutionTool] Query failed: {Error}", error);
                memory.Errors.Add($"SQL execution error: {error}");
                return ToolResult.Fail(error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SqlExecutionTool] Exception during SQL execution");
            memory.Errors.Add($"Execution exception: {ex.Message}");
            return ToolResult.Fail($"SQL execution failed: {ex.Message}");
        }
    }
}
