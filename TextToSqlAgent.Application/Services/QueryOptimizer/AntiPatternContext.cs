namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Context information for intelligent anti-pattern suppression
/// </summary>
public class AntiPatternContext
{
    /// <summary>
    /// Suppress AP-23 (missing WHERE) for analytical queries
    /// </summary>
    public bool IsAnalyticalQuery { get; set; }

    /// <summary>
    /// Reduce severity of AP-07 (DISTINCT) when unique constraints exist
    /// </summary>
    public bool HasUniqueConstraints { get; set; }

    /// <summary>
    /// Suppress AP-08 (UNION) for reporting queries
    /// </summary>
    public bool IsReportingQuery { get; set; }

    /// <summary>
    /// Query intent classification
    /// </summary>
    public QueryIntent Intent { get; set; }

    /// <summary>
    /// Estimated table rows (0 = unknown)
    /// </summary>
    public int EstimatedTableRows { get; set; }
}

public enum QueryIntent
{
    Query,
    Write,
    DDL
}
