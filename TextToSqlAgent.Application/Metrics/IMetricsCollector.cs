namespace TextToSqlAgent.Application.Metrics;

/// <summary>
/// Interface for collecting and retrieving query metrics
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Record query metrics after execution
    /// </summary>
    /// <param name="metrics">The query metrics to record</param>
    void Record(QueryMetrics metrics);

    /// <summary>
    /// Get summary statistics for a time period
    /// </summary>
    /// <param name="period">Time period to analyze</param>
    /// <returns>Metrics summary</returns>
    Task<MetricsSummary> GetSummaryAsync(TimeSpan period);

    /// <summary>
    /// Get escalation statistics
    /// </summary>
    /// <returns>Escalation statistics</returns>
    Task<EscalationStats> GetEscalationStatsAsync();

    /// <summary>
    /// Get all recorded query metrics
    /// </summary>
    /// <param name="period">Optional time period filter</param>
    /// <returns>List of query metrics</returns>
    Task<IReadOnlyList<QueryMetrics>> GetMetricsAsync(TimeSpan? period = null);

    /// <summary>
    /// Reset all metrics
    /// </summary>
    void Reset();

    /// <summary>
    /// Record cache hit
    /// </summary>
    void RecordCacheHit(long lookupTimeMs);

    /// <summary>
    /// Record cache miss
    /// </summary>
    void RecordCacheMiss(long lookupTimeMs);
}
