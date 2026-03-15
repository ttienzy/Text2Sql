namespace TextToSqlAgent.Application.Metrics;

using Microsoft.Extensions.Logging;

/// <summary>
/// In-memory implementation of query metrics collector
/// Tracks query distribution, LLM calls, escalations, latency, and cache effectiveness
/// </summary>
public class MetricsCollector : IMetricsCollector
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly List<QueryMetrics> _metrics = new();
    private readonly object _lock = new();

    // Cache statistics
    private int _cacheHits;
    private int _cacheMisses;
    private long _totalCacheLookupTime;
    private readonly object _cacheLock = new();

    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Record(QueryMetrics metrics)
    {
        lock (_lock)
        {
            _metrics.Add(metrics);

            // Keep only last 10,000 metrics to prevent memory issues
            if (_metrics.Count > 10000)
            {
                _metrics.RemoveAt(0);
            }
        }

        _logger.LogDebug(
            "Recorded query metrics: Complexity={Complexity}, LlmCalls={LlmCalls}, " +
            "Latency={Latency}ms, Success={Success}, Escalated={Escalated}",
            metrics.QueryComplexity,
            metrics.LlmCallCount,
            metrics.LatencyMs,
            metrics.IsSuccess,
            metrics.WasEscalated);
    }

    /// <inheritdoc />
    public Task<MetricsSummary> GetSummaryAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        List<QueryMetrics> relevantMetrics;

        lock (_lock)
        {
            relevantMetrics = _metrics
                .Where(m => m.Timestamp >= cutoff)
                .ToList();
        }

        var summary = new MetricsSummary
        {
            TotalQueries = relevantMetrics.Count,
            PeriodStart = relevantMetrics.Any() ? relevantMetrics.Min(m => m.Timestamp) : cutoff,
            PeriodEnd = relevantMetrics.Any() ? relevantMetrics.Max(m => m.Timestamp) : DateTime.UtcNow
        };

        if (relevantMetrics.Count == 0)
        {
            return Task.FromResult(summary);
        }

        // Calculate by complexity distribution
        var complexityGroups = relevantMetrics.GroupBy(m => m.QueryComplexity);
        foreach (var group in complexityGroups)
        {
            summary.ByComplexity[group.Key] = group.Count();
            summary.ByComplexityPercent[group.Key] = (double)group.Count() / relevantMetrics.Count * 100;
        }

        // Calculate average latency
        summary.AvgLatencyMs = relevantMetrics.Average(m => m.LatencyMs);

        // Calculate average LLM calls
        summary.AvgLlmCalls = relevantMetrics.Average(m => m.LlmCallCount);

        // Calculate success rate
        summary.SuccessRate = (double)relevantMetrics.Count(m => m.IsSuccess) / relevantMetrics.Count;

        // Calculate escalation stats
        var escalations = relevantMetrics.Where(m => m.WasEscalated).ToList();
        summary.Escalation.TotalEscalations = escalations.Count;
        summary.Escalation.EscalationRate = (double)escalations.Count / relevantMetrics.Count;

        // Track escalation paths
        var escalationPaths = relevantMetrics
            .Where(m => m.WasEscalated && !string.IsNullOrEmpty(m.EscalationReason))
            .SelectMany(m => ParseEscalationPath(m.EscalationReason!))
            .ToList();

        summary.Escalation.SimpleToMedium = escalationPaths.Count(p => p.From == "Simple" && p.To == "Medium");
        summary.Escalation.MediumToComplex = escalationPaths.Count(p => p.From == "Medium" && p.To == "Complex");
        summary.Escalation.FailedAfterEscalation = escalations.Count(e => !e.IsSuccess);

        // Calculate per-complexity stats
        foreach (var complexity in new[] { "Simple", "Medium", "Complex" })
        {
            var complexMetrics = relevantMetrics.Where(m => m.QueryComplexity == complexity).ToList();
            if (complexMetrics.Any())
            {
                summary.AvgLatencyByComplexity[complexity] = complexMetrics.Average(m => m.LatencyMs);
                summary.AvgLlmCallsByComplexity[complexity] = complexMetrics.Average(m => m.LlmCallCount);
                summary.ErrorRateByComplexity[complexity] = (double)complexMetrics.Count(m => !m.IsSuccess) / complexMetrics.Count;
            }
        }

        // Calculate cache stats
        lock (_cacheLock)
        {
            summary.Cache.Hits = _cacheHits;
            summary.Cache.Misses = _cacheMisses;
            var totalCacheOps = _cacheHits + _cacheMisses;
            summary.Cache.HitRate = totalCacheOps > 0 ? (double)_cacheHits / totalCacheOps : 0;
            summary.Cache.AvgLookupTimeMs = totalCacheOps > 0 ? (double)_totalCacheLookupTime / totalCacheOps : 0;
        }

        return Task.FromResult(summary);
    }

    /// <inheritdoc />
    public Task<EscalationStats> GetEscalationStatsAsync()
    {
        List<QueryMetrics> allMetrics;
        lock (_lock)
        {
            allMetrics = _metrics.ToList();
        }

        var stats = new EscalationStats();

        if (allMetrics.Count == 0)
        {
            return Task.FromResult(stats);
        }

        var escalations = allMetrics.Where(m => m.WasEscalated).ToList();
        stats.TotalEscalations = escalations.Count;
        stats.EscalationRate = (double)escalations.Count / allMetrics.Count;

        var paths = escalations
            .Where(m => !string.IsNullOrEmpty(m.EscalationReason))
            .SelectMany(m => ParseEscalationPath(m.EscalationReason!))
            .ToList();

        stats.SimpleToMedium = paths.Count(p => p.From == "Simple" && p.To == "Medium");
        stats.MediumToComplex = paths.Count(p => p.From == "Medium" && p.To == "Complex");
        stats.FailedAfterEscalation = escalations.Count(e => !e.IsSuccess);

        return Task.FromResult(stats);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueryMetrics>> GetMetricsAsync(TimeSpan? period = null)
    {
        List<QueryMetrics> result;

        lock (_lock)
        {
            if (period.HasValue)
            {
                var cutoff = DateTime.UtcNow - period.Value;
                result = _metrics.Where(m => m.Timestamp >= cutoff).ToList();
            }
            else
            {
                result = _metrics.ToList();
            }
        }

        return Task.FromResult<IReadOnlyList<QueryMetrics>>(result);
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_lock)
        {
            _metrics.Clear();
        }

        lock (_cacheLock)
        {
            _cacheHits = 0;
            _cacheMisses = 0;
            _totalCacheLookupTime = 0;
        }

        _logger.LogInformation("Query metrics reset");
    }

    /// <inheritdoc />
    public void RecordCacheHit(long lookupTimeMs)
    {
        lock (_cacheLock)
        {
            _cacheHits++;
            _totalCacheLookupTime += lookupTimeMs;
        }
    }

    /// <inheritdoc />
    public void RecordCacheMiss(long lookupTimeMs)
    {
        lock (_cacheLock)
        {
            _cacheMisses++;
            _totalCacheLookupTime += lookupTimeMs;
        }
    }

    /// <summary>
    /// Parse escalation reason to extract complexity transition
    /// </summary>
    private List<(string From, string To)> ParseEscalationPath(string reason)
    {
        var paths = new List<(string From, string To)>();

        // Parse patterns like "Escalated from Simple: reason" or "from {From} to {To}"
        if (reason.Contains("Simple") && reason.Contains("Medium"))
        {
            paths.Add(("Simple", "Medium"));
        }
        if (reason.Contains("Medium") && reason.Contains("Complex"))
        {
            paths.Add(("Medium", "Complex"));
        }
        if (reason.Contains("Simple") && reason.Contains("Complex"))
        {
            paths.Add(("Simple", "Complex"));
        }

        return paths;
    }
}
