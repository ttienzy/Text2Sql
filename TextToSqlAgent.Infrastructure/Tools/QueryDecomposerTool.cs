using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Tools;

/// <summary>
/// Tool for decomposing complex queries into simpler sub-queries
/// </summary>
public class QueryDecomposerTool : ITool
{
    private readonly ILLMClient _llm;
    private readonly ILogger<QueryDecomposerTool> _logger;

    public string Name => "decompose_query";

    public string Description => @"Decompose complex questions into simpler sub-queries.
Use this tool when the question requires multiple steps or complex logic.
Input: question (string)
Output: DecomposedQuery with sub-queries and dependencies";

    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new()
            {
                Name = "question",
                Type = "string",
                Description = "The complex question to decompose",
                Required = true
            }
        }
    };

    public QueryDecomposerTool(ILLMClient llm, ILogger<QueryDecomposerTool> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var question = input.GetString("question", "query");
            _logger.LogInformation("[QueryDecomposerTool] Decomposing: {Question}", question);

            var prompt = BuildPrompt(question);
            var response = await _llm.CompleteAsync(prompt, ct);

            var decomposed = ParseResponse(response);

            if (decomposed.RequiresDecomposition)
            {
                _logger.LogInformation("[QueryDecomposerTool] Decomposed into {Count} sub-queries",
                    decomposed.SubQueries.Count);
            }
            else
            {
                _logger.LogInformation("[QueryDecomposerTool] Query is simple, no decomposition needed");
            }

            return ToolResult.FromSuccess(decomposed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryDecomposerTool] Error decomposing query");
            return ToolResult.FromError($"Failed to decompose query: {ex.Message}");
        }
    }

    private string BuildPrompt(string question)
    {
        return $@"Analyze if this question requires decomposition into sub-queries.

Question: ""{question}""

Complex queries that need decomposition:
- Comparisons (e.g., ""compare X vs Y"")
- Multiple aggregations (e.g., ""top 5 and bottom 5"")
- Nested conditions (e.g., ""customers who bought X but not Y"")
- Time-based comparisons (e.g., ""this year vs last year"")
- Multi-step calculations (e.g., ""growth rate"", ""percentage"")

Simple queries that DON'T need decomposition:
- Single aggregation (e.g., ""count customers"")
- Simple filter (e.g., ""customers from New York"")
- Basic JOIN (e.g., ""orders with customer names"")
- Top N (e.g., ""top 10 products"")

Return ONLY valid JSON:
{{
  ""is_complex"": true/false,
  ""reasoning"": ""why this is/isn't complex"",
  ""sub_queries"": [
    {{
      ""step"": 1,
      ""description"": ""What this step does"",
      ""question"": ""Simplified question for this step"",
      ""dependencies"": []
    }},
    {{
      ""step"": 2,
      ""description"": ""What this step does"",
      ""question"": ""Simplified question for this step"",
      ""dependencies"": [1]
    }}
  ]
}}

If not complex, return empty sub_queries array.";
    }

    private DecomposedQuery ParseResponse(string response)
    {
        try
        {
            var json = response.Replace("```json", "").Replace("```", "").Trim();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var decomposed = new DecomposedQuery
            {
                IsComplex = root.GetProperty("is_complex").GetBoolean(),
                Reasoning = root.GetProperty("reasoning").GetString() ?? ""
            };

            if (root.TryGetProperty("sub_queries", out var subQueries))
            {
                foreach (var sq in subQueries.EnumerateArray())
                {
                    var subQuery = new SubQuery
                    {
                        Step = sq.GetProperty("step").GetInt32(),
                        Description = sq.GetProperty("description").GetString() ?? "",
                        Question = sq.GetProperty("question").GetString() ?? ""
                    };

                    if (sq.TryGetProperty("dependencies", out var deps))
                    {
                        foreach (var dep in deps.EnumerateArray())
                        {
                            subQuery.Dependencies.Add(dep.GetInt32());
                        }
                    }

                    decomposed.SubQueries.Add(subQuery);
                }
            }

            return decomposed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryDecomposerTool] Failed to parse response: {Response}", response);
            return new DecomposedQuery { IsComplex = false };
        }
    }
}
