using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.Security;

/// <summary>
/// Estimates query execution cost and prevents expensive queries
/// </summary>
public class QueryCostEstimator
{
    private readonly QueryComplexityAnalyzer _complexityAnalyzer;
    private readonly ILogger<QueryCostEstimator> _logger;
    private readonly CostLimits _limits;

    public QueryCostEstimator(
        QueryComplexityAnalyzer complexityAnalyzer,
        ILogger<QueryCostEstimator> logger,
        CostLimits? limits = null)
    {
        _complexityAnalyzer = complexityAnalyzer;
        _logger = logger;
        _limits = limits ?? new CostLimits();
    }

    /// <summary>
    /// Estimate query cost and check if it's allowed
    /// </summary>
    public CostEstimation EstimateCost(string sql)
    {
        var complexity = _complexityAnalyzer.Analyze(sql);

        var estimation = new CostEstimation
        {
            Sql = sql,
            ComplexityScore = complexity.ComplexityScore,
            EstimatedCost = complexity.EstimatedCost,
            ComplexityLevel = complexity.Level,
            IsAllowed = true,
            Warnings = new List<string>()
        };

        // Check against limits
        if (complexity.ComplexityScore > _limits.MaxComplexityScore)
        {
            estimation.IsAllowed = false;
            estimation.DenialReason = $"Query complexity ({complexity.ComplexityScore:F1}) exceeds limit ({_limits.MaxComplexityScore})";
            _logger.LogWarning("Query denied: {Reason}", estimation.DenialReason);
        }

        if (complexity.EstimatedCost > _limits.MaxEstimatedCost)
        {
            estimation.IsAllowed = false;
            estimation.DenialReason = $"Estimated cost ({complexity.EstimatedCost:F0}) exceeds limit ({_limits.MaxEstimatedCost})";
            _logger.LogWarning("Query denied: {Reason}", estimation.DenialReason);
        }

        if (complexity.JoinCount > _limits.MaxJoins)
        {
            estimation.IsAllowed = false;
            estimation.DenialReason = $"Too many JOINs ({complexity.JoinCount}) - limit is {_limits.MaxJoins}";
            _logger.LogWarning("Query denied: {Reason}", estimation.DenialReason);
        }

        if (complexity.SubqueryCount > _limits.MaxSubqueries)
        {
            estimation.IsAllowed = false;
            estimation.DenialReason = $"Too many subqueries ({complexity.SubqueryCount}) - limit is {_limits.MaxSubqueries}";
            _logger.LogWarning("Query denied: {Reason}", estimation.DenialReason);
        }

        // Add warnings for queries near limits
        if (estimation.IsAllowed)
        {
            if (complexity.ComplexityScore > _limits.MaxComplexityScore * 0.8)
            {
                estimation.Warnings.Add($"Query complexity is high ({complexity.ComplexityScore:F1})");
            }

            if (complexity.EstimatedCost > _limits.MaxEstimatedCost * 0.8)
            {
                estimation.Warnings.Add($"Estimated cost is high ({complexity.EstimatedCost:F0})");
            }
        }

        // Estimate execution time
        estimation.EstimatedExecutionTimeMs = EstimateExecutionTime(complexity.EstimatedCost);

        return estimation;
    }

    /// <summary>
    /// Estimate execution time based on cost
    /// </summary>
    private long EstimateExecutionTime(double cost)
    {
        // Simple linear estimation: cost * 10ms
        // In production, this should be calibrated with actual query performance data
        return (long)(cost * 10);
    }

    /// <summary>
    /// Check if query should have timeout
    /// </summary>
    public int? GetRecommendedTimeout(CostEstimation estimation)
    {
        if (estimation.EstimatedExecutionTimeMs < 1000)
            return null; // No timeout needed for fast queries

        if (estimation.EstimatedExecutionTimeMs < 5000)
            return 10000; // 10 second timeout

        if (estimation.EstimatedExecutionTimeMs < 10000)
            return 30000; // 30 second timeout

        return 60000; // 60 second timeout for expensive queries
    }
}

/// <summary>
/// Cost estimation result
/// </summary>
public class CostEstimation
{
    public string Sql { get; set; } = string.Empty;
    public double ComplexityScore { get; set; }
    public double EstimatedCost { get; set; }
    public ComplexityLevel ComplexityLevel { get; set; }
    public long EstimatedExecutionTimeMs { get; set; }
    public bool IsAllowed { get; set; }
    public string? DenialReason { get; set; }
    public List<string> Warnings { get; set; } = new();

    public override string ToString()
    {
        var result = $"Cost Estimation:\n";
        result += $"  Complexity: {ComplexityLevel} (Score: {ComplexityScore:F1})\n";
        result += $"  Estimated Cost: {EstimatedCost:F0}\n";
        result += $"  Estimated Time: {EstimatedExecutionTimeMs}ms\n";
        result += $"  Allowed: {(IsAllowed ? "✓ Yes" : "✗ No")}\n";

        if (!IsAllowed && !string.IsNullOrEmpty(DenialReason))
        {
            result += $"  Reason: {DenialReason}\n";
        }

        if (Warnings.Count > 0)
        {
            result += "  Warnings:\n";
            foreach (var warning in Warnings)
            {
                result += $"    - {warning}\n";
            }
        }

        return result;
    }
}

/// <summary>
/// Query cost limits configuration
/// </summary>
public class CostLimits
{
    public double MaxComplexityScore { get; set; } = 25.0;
    public double MaxEstimatedCost { get; set; } = 1000.0;
    public int MaxJoins { get; set; } = 8;
    public int MaxSubqueries { get; set; } = 5;
    public int MaxExecutionTimeMs { get; set; } = 30000; // 30 seconds
}
