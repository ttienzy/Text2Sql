using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Models;
using System.Text.Json;

namespace TextToSqlAgent.Application.Services.Advanced;

/// <summary>
/// Query decomposition for complex multi-step queries
/// Inspired by MARS-SQL paper (2024)
/// Breaks down complex queries into sequential sub-tasks
/// </summary>
public class QueryDecomposer
{
    private readonly ILogger<QueryDecomposer> _logger;
    private readonly Kernel? _kernel;

    public QueryDecomposer(
        ILogger<QueryDecomposer> logger,
        Kernel? kernel = null)
    {
        _logger = logger;
        _kernel = kernel;
    }

    /// <summary>
    /// Decompose complex query into sequential sub-tasks
    /// </summary>
    public async Task<DecomposedQuery?> DecomposeAsync(
        string complexQuery,
        DatabaseSchema schema)
    {
        _logger.LogInformation(
            "[QueryDecomposer] Decomposing complex query: {Query}",
            complexQuery.Length > 100 ? complexQuery.Substring(0, 100) + "..." : complexQuery);

        if (_kernel == null)
        {
            _logger.LogWarning("[QueryDecomposer] Kernel not available, cannot decompose query");
            return null;
        }

        try
        {
            var schemaContext = FormatSchemaContext(schema);

            var prompt = $@"You are a SQL query planner. Decompose this complex query into sequential sub-tasks.

User Query: {complexQuery}

Database Schema:
{schemaContext}

Analyze the query and break it down into logical steps:
1. Identify what data needs to be retrieved first
2. What calculations or transformations are needed
3. How results should be combined or compared

Respond with JSON:
{{
  ""is_complex"": true/false,
  ""sub_tasks"": [
    {{
      ""step"": 1,
      ""description"": ""Brief description of what this step does"",
      ""sql_needed"": true/false,
      ""depends_on"": []
    }}
  ],
  ""final_aggregation"": ""How to combine results (if needed)""
}}

If the query is simple (single SELECT), return is_complex: false.
For complex queries (comparisons, trends, multi-step), break it down.";

            var response = await _kernel.InvokePromptAsync(prompt);
            var result = response.ToString();

            var decomposed = ParseDecomposition(result);

            if (decomposed != null && !decomposed.IsComplex)
            {
                _logger.LogDebug("[QueryDecomposer] Query is simple, no decomposition needed");
                return null;
            }

            _logger.LogInformation(
                "[QueryDecomposer] Decomposed into {Count} sub-tasks",
                decomposed?.SubTasks.Count ?? 0);

            return decomposed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryDecomposer] Failed to decompose query");
            return null;
        }
    }

    /// <summary>
    /// Execute decomposed query (placeholder for future implementation)
    /// </summary>
    public async Task<DecomposedQueryResult> ExecuteDecomposedAsync(
        DecomposedQuery decomposed,
        DatabaseSchema schema)
    {
        _logger.LogInformation(
            "[QueryDecomposer] Executing {Count} sub-tasks",
            decomposed.SubTasks.Count);

        // TODO: Implement actual execution logic
        // For now, return placeholder result

        await Task.CompletedTask;

        return new DecomposedQueryResult
        {
            Success = false,
            Message = "Query decomposition execution not yet implemented"
        };
    }

    #region Helper Methods

    private string FormatSchemaContext(DatabaseSchema schema)
    {
        var tables = schema.Tables.Take(10).Select(t =>
            $"{t.TableName} ({t.Columns.Count} columns)");

        return string.Join(", ", tables);
    }

    private DecomposedQuery? ParseDecomposition(string response)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<DecomposedQuery>(response, options);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[QueryDecomposer] JSON parsing failed");

            // Check if query is marked as simple
            if (response.Contains("\"is_complex\": false", StringComparison.OrdinalIgnoreCase))
            {
                return new DecomposedQuery
                {
                    IsComplex = false,
                    SubTasks = [],
                    FinalAggregation = ""
                };
            }

            return null;
        }
    }

    #endregion
}

/// <summary>
/// Decomposed query with sub-tasks
/// </summary>
public class DecomposedQuery
{
    public bool IsComplex { get; set; }
    public List<SubTask> SubTasks { get; set; } = [];
    public string FinalAggregation { get; set; } = "";
}

/// <summary>
/// Individual sub-task in decomposed query
/// </summary>
public class SubTask
{
    public int Step { get; set; }
    public string Description { get; set; } = "";
    public bool SqlNeeded { get; set; }
    public List<int> DependsOn { get; set; } = [];
}

/// <summary>
/// Result of executing decomposed query
/// </summary>
public class DecomposedQueryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public Dictionary<int, object>? IntermediateResults { get; set; }
    public object? FinalResult { get; set; }
}
