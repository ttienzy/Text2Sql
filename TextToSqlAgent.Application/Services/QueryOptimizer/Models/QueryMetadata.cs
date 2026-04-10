using System;
using System.Collections.Generic;
using System.Linq;

namespace TextToSqlAgent.Application.Services.QueryOptimizer.Models;

/// <summary>
/// Metadata extracted from SQL query via AST parsing
/// </summary>
public class QueryMetadata
{
    public List<string> Tables { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public int JoinCount { get; set; }
    public int SubqueryCount { get; set; }
    public int WindowFunctionCount { get; set; }
    public int CteCount { get; set; }
    public List<AntiPattern> DetectedIssues { get; set; } = new();
    public int ComplexityScore { get; set; }

    // Phase 2: Critical columns for statistics analysis
    public List<string> WhereColumns { get; set; } = new();
    public List<string> JoinColumns { get; set; } = new();
    public List<string> OrderByColumns { get; set; } = new();
    public List<string> GroupByColumns { get; set; } = new();

    /// <summary>
    /// Get all critical columns (deduplicated union of WHERE, JOIN, ORDER BY, GROUP BY)
    /// </summary>
    public List<string> GetCriticalColumns()
    {
        var critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        critical.UnionWith(WhereColumns);
        critical.UnionWith(JoinColumns);
        critical.UnionWith(OrderByColumns);
        critical.UnionWith(GroupByColumns);
        return critical.ToList();
    }
}

/// <summary>
/// Represents a detected anti-pattern in SQL query
/// </summary>
public class AntiPattern
{
    public string Code { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public int? Location { get; set; }
    public string? AutoFixSuggestion { get; set; }
    public ConfidenceLevel ConfidenceLevel { get; set; } = ConfidenceLevel.Low;
    public bool SuppressInAnalyticalContext { get; set; }
    public PatternCategory Category { get; set; }
}

public enum Severity
{
    Critical,
    Serious,
    Warning,
    Info,
    Error
}

public enum ConfidenceLevel
{
    High,
    Medium,
    Low
}

public enum PatternCategory
{
    SARGability,
    IndexUsage,
    CodeQuality,
    Logic,
    Performance
}
