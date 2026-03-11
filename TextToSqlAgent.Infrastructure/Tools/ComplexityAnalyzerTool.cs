using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.Tools;

/// <summary>
/// Tool for analyzing SQL query complexity
/// </summary>
public class ComplexityAnalyzerTool : ITool
{
    private readonly QueryComplexityAnalyzer _analyzer;
    private readonly ILogger<ComplexityAnalyzerTool> _logger;

    public string Name => "analyze_complexity";
    public string Description => "Analyze SQL query complexity and get optimization suggestions";

    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new() {
                Name = "sql",
                Type = "string",
                Description = "The SQL query to analyze",
                Required = true
            }
        }
    };

    public ComplexityAnalyzerTool(
        QueryComplexityAnalyzer analyzer,
        ILogger<ComplexityAnalyzerTool> logger)
    {
        _analyzer = analyzer;
        _logger = logger;
    }

    public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var sql = input.GetString("sql", "query");

            _logger.LogInformation("Analyzing query complexity");

            var analysis = _analyzer.Analyze(sql);

            _logger.LogInformation(
                "Query complexity: {Level} (Score: {Score:F1})",
                analysis.Level,
                analysis.ComplexityScore);

            return Task.FromResult(ToolResult.FromSuccess(new
            {
                level = analysis.Level.ToString(),
                score = analysis.ComplexityScore,
                estimated_cost = analysis.EstimatedCost,
                features = new
                {
                    joins = analysis.JoinCount,
                    subqueries = analysis.SubqueryCount,
                    aggregations = analysis.AggregationCount,
                    window_functions = analysis.WindowFunctionCount,
                    ctes = analysis.CteCount,
                    has_group_by = analysis.HasGroupBy,
                    has_order_by = analysis.HasOrderBy,
                    has_distinct = analysis.HasDistinct
                },
                warnings = analysis.Warnings,
                optimization_suggestions = analysis.OptimizationSuggestions,
                summary = analysis.ToString()
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Complexity analysis failed");
            return Task.FromResult(ToolResult.FromError($"Analysis failed: {ex.Message}"));
        }
    }
}
