namespace TextToSqlAgent.Application.Metrics;

/// <summary>
/// Query-level metrics for monitoring and analytics
/// Tracks complexity, LLM calls, latency, escalations, and errors
/// </summary>
public class QueryMetrics
{
    /// <summary>
    /// Query complexity classification: Simple/Medium/Complex
    /// </summary>
    public string QueryComplexity { get; set; } = string.Empty;

    /// <summary>
    /// Actual number of LLM calls made during execution
    /// </summary>
    public int LlmCallCount { get; set; }

    /// <summary>
    /// Total latency in milliseconds
    /// </summary>
    public long LatencyMs { get; set; }

    /// <summary>
    /// Whether the query was escalated to a higher complexity pipeline
    /// </summary>
    public bool WasEscalated { get; set; }

    /// <summary>
    /// Reason for escalation if applicable
    /// </summary>
    public string? EscalationReason { get; set; }

    /// <summary>
    /// Whether the query executed successfully
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when the query was processed
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Pipeline used to execute the query (SimplePipeline/MediumPipeline/ComplexPipeline)
    /// </summary>
    public string? PipelineUsed { get; set; }

    /// <summary>
    /// Classification method used (RuleBased/LLM)
    /// </summary>
    public string? ClassificationMethod { get; set; }
}

/// <summary>
/// Summary statistics for query metrics over a time period
/// </summary>
public class MetricsSummary
{
    /// <summary>
    /// Total number of queries in the period
    /// </summary>
    public int TotalQueries { get; set; }

    /// <summary>
    /// Query distribution by complexity
    /// </summary>
    public Dictionary<string, int> ByComplexity { get; set; } = new();

    /// <summary>
    /// Query distribution by complexity as percentage
    /// </summary>
    public Dictionary<string, double> ByComplexityPercent { get; set; } = new();

    /// <summary>
    /// Average latency in milliseconds
    /// </summary>
    public double AvgLatencyMs { get; set; }

    /// <summary>
    /// Average LLM calls per query
    /// </summary>
    public double AvgLlmCalls { get; set; }

    /// <summary>
    /// Success rate (0-1)
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Escalation statistics
    /// </summary>
    public EscalationStats Escalation { get; set; } = new();

    /// <summary>
    /// Cache statistics
    /// </summary>
    public CacheStats Cache { get; set; } = new();

    /// <summary>
    /// Average latency by complexity tier
    /// </summary>
    public Dictionary<string, double> AvgLatencyByComplexity { get; set; } = new();

    /// <summary>
    /// Average LLM calls by complexity tier
    /// </summary>
    public Dictionary<string, double> AvgLlmCallsByComplexity { get; set; } = new();

    /// <summary>
    /// Error rate by complexity tier
    /// </summary>
    public Dictionary<string, double> ErrorRateByComplexity { get; set; } = new();

    /// <summary>
    /// Time period start
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Time period end
    /// </summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>
    /// When the summary was generated
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Statistics about query escalations
/// </summary>
public class EscalationStats
{
    /// <summary>
    /// Total number of escalations
    /// </summary>
    public int TotalEscalations { get; set; }

    /// <summary>
    /// Escalation rate (0-1)
    /// </summary>
    public double EscalationRate { get; set; }

    /// <summary>
    /// Number of Simple to Medium escalations
    /// </summary>
    public int SimpleToMedium { get; set; }

    /// <summary>
    /// Number of Medium to Complex escalations
    /// </summary>
    public int MediumToComplex { get; set; }

    /// <summary>
    /// Number of escalations that failed after escalation
    /// </summary>
    public int FailedAfterEscalation { get; set; }
}

/// <summary>
/// Statistics about schema cache effectiveness
/// </summary>
public class CacheStats
{
    /// <summary>
    /// Number of cache hits
    /// </summary>
    public int Hits { get; set; }

    /// <summary>
    /// Number of cache misses
    /// </summary>
    public int Misses { get; set; }

    /// <summary>
    /// Cache hit rate (0-1)
    /// </summary>
    public double HitRate { get; set; }

    /// <summary>
    /// Average cache lookup time in milliseconds
    /// </summary>
    public double AvgLookupTimeMs { get; set; }
}
