using System.Collections.Generic;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.API.DTOs.QueryOptimizer;

/// <summary>
/// Response from query optimization
/// </summary>
public class OptimizeQueryResponse
{
    /// <summary>
    /// Original SQL query
    /// </summary>
    public string OriginalSql { get; set; } = string.Empty;

    /// <summary>
    /// Optimized SQL query
    /// </summary>
    public string OptimizedSql { get; set; } = string.Empty;

    /// <summary>
    /// Whether query was changed
    /// </summary>
    public bool IsChanged { get; set; }

    /// <summary>
    /// Overall severity of detected issues
    /// </summary>
    public string Severity { get; set; } = "ok";

    /// <summary>
    /// List of detected anti-patterns
    /// </summary>
    public List<AntiPatternDto> DetectedIssues { get; set; } = new();

    /// <summary>
    /// List of issues that were fixed
    /// </summary>
    public List<string> IssuesFixed { get; set; } = new();

    /// <summary>
    /// Vietnamese explanation of changes
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Estimated performance improvement
    /// </summary>
    public string EstimatedImprovement { get; set; } = string.Empty;

    /// <summary>
    /// Index suggestions
    /// </summary>
    public List<string> IndexSuggestions { get; set; } = new();

    /// <summary>
    /// Query complexity score
    /// </summary>
    public int ComplexityScore { get; set; }

    /// <summary>
    /// Model used for optimization
    /// </summary>
    public string ModelUsed { get; set; } = string.Empty;

    /// <summary>
    /// Pre-flight execution plan analysis (if available)
    /// </summary>
    public PreFlightAnalysis? PreFlightAnalysis { get; set; }
}

/// <summary>
/// Anti-pattern DTO
/// </summary>
public class AntiPatternDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public int? Location { get; set; }
}
