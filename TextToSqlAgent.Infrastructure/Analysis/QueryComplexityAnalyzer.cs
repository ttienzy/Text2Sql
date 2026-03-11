using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace TextToSqlAgent.Infrastructure.Analysis;

/// <summary>
/// Analyzes SQL query complexity for cost estimation and optimization
/// </summary>
public class QueryComplexityAnalyzer
{
    private readonly ILogger<QueryComplexityAnalyzer> _logger;

    public QueryComplexityAnalyzer(ILogger<QueryComplexityAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze query complexity
    /// </summary>
    public ComplexityAnalysis Analyze(string sql)
    {
        _logger.LogDebug("Analyzing query complexity");

        var analysis = new ComplexityAnalysis
        {
            Sql = sql
        };

        var lowerSql = sql.ToLower();

        // Count various SQL features
        analysis.JoinCount = CountJoins(lowerSql);
        analysis.SubqueryCount = CountSubqueries(sql);
        analysis.AggregationCount = CountAggregations(lowerSql);
        analysis.WindowFunctionCount = CountWindowFunctions(lowerSql);
        analysis.UnionCount = CountUnions(lowerSql);
        analysis.CteCount = CountCTEs(lowerSql);
        analysis.HasGroupBy = lowerSql.Contains("group by");
        analysis.HasOrderBy = lowerSql.Contains("order by");
        analysis.HasDistinct = lowerSql.Contains("distinct");
        analysis.HasHaving = lowerSql.Contains("having");

        // Calculate complexity score
        analysis.ComplexityScore = CalculateComplexityScore(analysis);
        analysis.Level = DetermineComplexityLevel(analysis.ComplexityScore);

        // Estimate execution cost
        analysis.EstimatedCost = EstimateExecutionCost(analysis);

        // Generate warnings
        analysis.Warnings = GenerateWarnings(analysis);

        // Optimization suggestions
        analysis.OptimizationSuggestions = GenerateOptimizationSuggestions(analysis);

        return analysis;
    }

    private int CountJoins(string sql)
    {
        var joinPattern = @"\b(inner\s+join|left\s+join|right\s+join|full\s+join|cross\s+join|join)\b";
        return Regex.Matches(sql, joinPattern, RegexOptions.IgnoreCase).Count;
    }

    private int CountSubqueries(string sql)
    {
        // Count SELECT statements (excluding the main one)
        var selectCount = Regex.Matches(sql, @"\bselect\b", RegexOptions.IgnoreCase).Count;
        return Math.Max(0, selectCount - 1);
    }

    private int CountAggregations(string sql)
    {
        var aggPattern = @"\b(count|sum|avg|min|max|string_agg|group_concat)\s*\(";
        return Regex.Matches(sql, aggPattern, RegexOptions.IgnoreCase).Count;
    }

    private int CountWindowFunctions(string sql)
    {
        var windowPattern = @"\b(row_number|rank|dense_rank|ntile|lag|lead|first_value|last_value)\s*\(";
        return Regex.Matches(sql, windowPattern, RegexOptions.IgnoreCase).Count;
    }

    private int CountUnions(string sql)
    {
        var unionPattern = @"\bunion(\s+all)?\b";
        return Regex.Matches(sql, unionPattern, RegexOptions.IgnoreCase).Count;
    }

    private int CountCTEs(string sql)
    {
        var ctePattern = @"\bwith\b";
        return Regex.Matches(sql, ctePattern, RegexOptions.IgnoreCase).Count;
    }

    private double CalculateComplexityScore(ComplexityAnalysis analysis)
    {
        double score = 1.0; // Base score

        // Add points for various features
        score += analysis.JoinCount * 2.0;
        score += analysis.SubqueryCount * 3.0;
        score += analysis.AggregationCount * 1.5;
        score += analysis.WindowFunctionCount * 2.5;
        score += analysis.UnionCount * 2.0;
        score += analysis.CteCount * 1.5;

        if (analysis.HasGroupBy) score += 1.5;
        if (analysis.HasOrderBy) score += 1.0;
        if (analysis.HasDistinct) score += 1.0;
        if (analysis.HasHaving) score += 1.5;

        return score;
    }

    private ComplexityLevel DetermineComplexityLevel(double score)
    {
        if (score <= 3.0) return ComplexityLevel.Simple;
        if (score <= 8.0) return ComplexityLevel.Medium;
        if (score <= 15.0) return ComplexityLevel.Complex;
        return ComplexityLevel.VeryComplex;
    }

    private double EstimateExecutionCost(ComplexityAnalysis analysis)
    {
        // Rough cost estimation (arbitrary units)
        double cost = 10.0; // Base cost

        cost += analysis.JoinCount * 50.0;
        cost += analysis.SubqueryCount * 100.0;
        cost += analysis.AggregationCount * 30.0;
        cost += analysis.WindowFunctionCount * 80.0;
        cost += analysis.UnionCount * 60.0;
        cost += analysis.CteCount * 40.0;

        if (analysis.HasGroupBy) cost += 40.0;
        if (analysis.HasOrderBy) cost += 30.0;
        if (analysis.HasDistinct) cost += 50.0;

        return cost;
    }

    private List<string> GenerateWarnings(ComplexityAnalysis analysis)
    {
        var warnings = new List<string>();

        if (analysis.JoinCount > 5)
        {
            warnings.Add($"High number of JOINs ({analysis.JoinCount}) - may impact performance");
        }

        if (analysis.SubqueryCount > 3)
        {
            warnings.Add($"Multiple subqueries ({analysis.SubqueryCount}) - consider using CTEs");
        }

        if (analysis.WindowFunctionCount > 0 && analysis.JoinCount > 3)
        {
            warnings.Add("Window functions with multiple JOINs - ensure proper indexing");
        }

        if (analysis.HasDistinct && analysis.JoinCount > 2)
        {
            warnings.Add("DISTINCT with multiple JOINs - may cause performance issues");
        }

        if (analysis.ComplexityScore > 20)
        {
            warnings.Add("Very complex query - consider breaking into smaller queries");
        }

        if (analysis.EstimatedCost > 500)
        {
            warnings.Add($"High estimated cost ({analysis.EstimatedCost:F0}) - query may be slow");
        }

        return warnings;
    }

    private List<string> GenerateOptimizationSuggestions(ComplexityAnalysis analysis)
    {
        var suggestions = new List<string>();

        if (analysis.SubqueryCount > 2)
        {
            suggestions.Add("Convert subqueries to CTEs for better readability and potential performance");
        }

        if (analysis.JoinCount > 4)
        {
            suggestions.Add("Review JOIN order and ensure proper indexes on join columns");
        }

        if (analysis.HasDistinct && analysis.HasGroupBy)
        {
            suggestions.Add("DISTINCT with GROUP BY may be redundant - review if both are needed");
        }

        if (analysis.WindowFunctionCount > 0)
        {
            suggestions.Add("Ensure window function partitions are indexed for better performance");
        }

        if (analysis.UnionCount > 0)
        {
            suggestions.Add("Consider if UNION ALL can be used instead of UNION for better performance");
        }

        if (analysis.Level == ComplexityLevel.VeryComplex)
        {
            suggestions.Add("Consider materializing intermediate results or using temporary tables");
        }

        return suggestions;
    }
}

/// <summary>
/// Query complexity analysis result
/// </summary>
public class ComplexityAnalysis
{
    public string Sql { get; set; } = string.Empty;
    public int JoinCount { get; set; }
    public int SubqueryCount { get; set; }
    public int AggregationCount { get; set; }
    public int WindowFunctionCount { get; set; }
    public int UnionCount { get; set; }
    public int CteCount { get; set; }
    public bool HasGroupBy { get; set; }
    public bool HasOrderBy { get; set; }
    public bool HasDistinct { get; set; }
    public bool HasHaving { get; set; }
    public double ComplexityScore { get; set; }
    public ComplexityLevel Level { get; set; }
    public double EstimatedCost { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> OptimizationSuggestions { get; set; } = new();

    public override string ToString()
    {
        var result = $"Complexity: {Level} (Score: {ComplexityScore:F1})\n";
        result += $"Estimated Cost: {EstimatedCost:F0}\n";
        result += "\nFeatures:\n";
        result += $"  JOINs: {JoinCount}\n";
        result += $"  Subqueries: {SubqueryCount}\n";
        result += $"  Aggregations: {AggregationCount}\n";
        result += $"  Window Functions: {WindowFunctionCount}\n";
        result += $"  CTEs: {CteCount}\n";

        if (Warnings.Count > 0)
        {
            result += "\n⚠ Warnings:\n";
            foreach (var warning in Warnings)
            {
                result += $"  - {warning}\n";
            }
        }

        if (OptimizationSuggestions.Count > 0)
        {
            result += "\n💡 Optimization Suggestions:\n";
            foreach (var suggestion in OptimizationSuggestions)
            {
                result += $"  - {suggestion}\n";
            }
        }

        return result;
    }
}

public enum ComplexityLevel
{
    Simple,
    Medium,
    Complex,
    VeryComplex
}
