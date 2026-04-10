using System.Collections.Generic;
using System.Linq;

namespace TextToSqlAgent.Application.Services.QueryOptimizer.Models;

/// <summary>
/// Pre-flight analysis result with full execution plan insights.
/// Includes permission check, warnings, cost drivers, and index recommendations.
/// </summary>
public class PreFlightAnalysis
{
    public double EstimatedCost { get; set; }
    public long EstimatedRows { get; set; }
    public bool CanGetExecutionPlan { get; set; } // false = no permission
    public List<CostDriver> CostDrivers { get; set; } = new();
    public List<PlanWarning> Warnings { get; set; } = new();
    public List<IndexRecommendation> IndexRecommendations { get; set; } = new();
    public List<ImplicitConversion> ImplicitConversions { get; set; } = new();
    public List<string> MissingStatistics { get; set; } = new();
    public bool NeedsOptimization { get; set; }
    public bool HasStaleStatistics { get; set; }
}

/// <summary>
/// Represents a cost driver in the execution plan.
/// Identifies expensive operations with actionable recommendations.
/// </summary>
public class CostDriver
{
    public string OperatorType { get; set; } = string.Empty;
    public double Cost { get; set; }
    public double Rows { get; set; }
    public string? ObjectName { get; set; }
    public string? IndexName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}

/// <summary>
/// Structured execution plan warning with type, severity, and recommendation.
/// </summary>
public class PlanWarning
{
    public string RawWarning { get; set; } = string.Empty;
    public WarningType Type { get; set; }
    public WarningSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

/// <summary>
/// Types of execution plan warnings.
/// </summary>
public enum WarningType
{
    MissingJoinPredicate,
    MissingStatistics,
    UnmatchedIndexes,
    SpillToTempDb,
    ImplicitConversion,
    StaleStatistics,
    Other
}

/// <summary>
/// Severity levels for execution plan warnings.
/// </summary>
public enum WarningSeverity
{
    Info,
    Medium,
    High,
    Critical
}

/// <summary>
/// Index recommendation extracted from execution plan.
/// </summary>
public class IndexRecommendation
{
    public string TableName { get; set; } = string.Empty;
    public List<string> KeyColumns { get; set; } = new();
    public List<string> IncludeColumns { get; set; } = new();
    public double ImpactPercentage { get; set; }
    public string CreateStatement { get; set; } = string.Empty;
}

/// <summary>
/// Implicit type conversion detected in execution plan.
/// Can prevent index usage and cause performance issues.
/// </summary>
public class ImplicitConversion
{
    public string ColumnName { get; set; } = string.Empty;
    public string FromType { get; set; } = string.Empty;
    public string ToType { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
}

/// <summary>
/// Missing index information from execution plan XML.
/// </summary>
public class MissingIndex
{
    public string Database { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public double Impact { get; set; }
    public List<string> EqualityColumns { get; set; } = new();
    public List<string> InequalityColumns { get; set; } = new();
    public List<string> IncludedColumns { get; set; } = new();

    /// <summary>
    /// Generate CREATE INDEX statement from missing index information.
    /// </summary>
    public string GenerateCreateStatement()
    {
        if (string.IsNullOrEmpty(Table) || (!EqualityColumns.Any() && !InequalityColumns.Any()))
        {
            return string.Empty;
        }

        var indexName = $"IX_{Table}_{string.Join("_", EqualityColumns.Concat(InequalityColumns).Take(3))}";
        var keyColumns = EqualityColumns.Concat(InequalityColumns).ToList();

        var sql = $"CREATE NONCLUSTERED INDEX [{indexName}]\n" +
                  $"ON [{Schema}].[{Table}] ({string.Join(", ", keyColumns.Select(c => $"[{c}]"))})";

        if (IncludedColumns.Any())
        {
            sql += $"\nINCLUDE ({string.Join(", ", IncludedColumns.Select(c => $"[{c}]"))})";
        }

        sql += $";\n-- Estimated Impact: {Impact:F2}%";

        return sql;
    }
}
