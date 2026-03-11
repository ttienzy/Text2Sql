using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace TextToSqlAgent.Infrastructure.Observability;

/// <summary>
/// Collects and aggregates metrics for monitoring
/// </summary>
public class MetricsCollector
{
    private readonly ILogger<MetricsCollector> _logger;
    private readonly ConcurrentDictionary<string, MetricData> _metrics;
    private readonly ConcurrentDictionary<string, ErrorData> _errors;
    private readonly object _lock = new();

    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger;
        _metrics = new ConcurrentDictionary<string, MetricData>();
        _errors = new ConcurrentDictionary<string, ErrorData>();
    }

    /// <summary>
    /// Record operation duration
    /// </summary>
    public void RecordDuration(string operation, long durationMs)
    {
        var metric = _metrics.GetOrAdd(operation, _ => new MetricData(operation));

        lock (metric.Lock)
        {
            metric.Count++;
            metric.TotalDuration += durationMs;
            metric.MinDuration = Math.Min(metric.MinDuration, durationMs);
            metric.MaxDuration = Math.Max(metric.MaxDuration, durationMs);
            metric.LastUpdated = DateTime.UtcNow;

            // Update percentiles (simple approximation)
            metric.Durations.Add(durationMs);
            if (metric.Durations.Count > 1000)
            {
                metric.Durations.RemoveAt(0); // Keep last 1000
            }
        }
    }

    /// <summary>
    /// Record custom metric
    /// </summary>
    public void RecordMetric(string metricName, double value, Dictionary<string, string>? tags = null)
    {
        var key = tags != null
            ? $"{metricName}_{string.Join("_", tags.Values)}"
            : metricName;

        var metric = _metrics.GetOrAdd(key, _ => new MetricData(metricName));

        lock (metric.Lock)
        {
            metric.Count++;
            metric.TotalValue += value;
            metric.MinValue = Math.Min(metric.MinValue, value);
            metric.MaxValue = Math.Max(metric.MaxValue, value);
            metric.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Record error occurrence
    /// </summary>
    public void RecordError(string operation, string errorType)
    {
        var key = $"{operation}_{errorType}";
        var error = _errors.GetOrAdd(key, _ => new ErrorData(operation, errorType));

        lock (error.Lock)
        {
            error.Count++;
            error.LastOccurrence = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Get metrics summary
    /// </summary>
    public MetricsSummary GetSummary()
    {
        var summary = new MetricsSummary
        {
            GeneratedAt = DateTime.UtcNow,
            Metrics = new List<MetricInfo>(),
            Errors = new List<ErrorInfo>()
        };

        foreach (var metric in _metrics.Values)
        {
            lock (metric.Lock)
            {
                var info = new MetricInfo
                {
                    Name = metric.Name,
                    Count = metric.Count,
                    AvgValue = metric.Count > 0 ? metric.TotalValue / metric.Count : 0,
                    MinValue = metric.MinValue,
                    MaxValue = metric.MaxValue,
                    AvgDuration = metric.Count > 0 ? metric.TotalDuration / metric.Count : 0,
                    MinDuration = metric.MinDuration,
                    MaxDuration = metric.MaxDuration,
                    P50Duration = CalculatePercentile(metric.Durations, 0.5),
                    P95Duration = CalculatePercentile(metric.Durations, 0.95),
                    P99Duration = CalculatePercentile(metric.Durations, 0.99),
                    LastUpdated = metric.LastUpdated
                };

                summary.Metrics.Add(info);
            }
        }

        foreach (var error in _errors.Values)
        {
            lock (error.Lock)
            {
                summary.Errors.Add(new ErrorInfo
                {
                    Operation = error.Operation,
                    ErrorType = error.ErrorType,
                    Count = error.Count,
                    LastOccurrence = error.LastOccurrence
                });
            }
        }

        return summary;
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        _metrics.Clear();
        _errors.Clear();
        _logger.LogInformation("Metrics reset");
    }

    /// <summary>
    /// Get metrics for specific operation
    /// </summary>
    public MetricInfo? GetMetric(string operation)
    {
        if (_metrics.TryGetValue(operation, out var metric))
        {
            lock (metric.Lock)
            {
                return new MetricInfo
                {
                    Name = metric.Name,
                    Count = metric.Count,
                    AvgDuration = metric.Count > 0 ? metric.TotalDuration / metric.Count : 0,
                    MinDuration = metric.MinDuration,
                    MaxDuration = metric.MaxDuration,
                    P50Duration = CalculatePercentile(metric.Durations, 0.5),
                    P95Duration = CalculatePercentile(metric.Durations, 0.95),
                    P99Duration = CalculatePercentile(metric.Durations, 0.99),
                    LastUpdated = metric.LastUpdated
                };
            }
        }

        return null;
    }

    private long CalculatePercentile(List<long> values, double percentile)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));

        return sorted[index];
    }
}

internal class MetricData
{
    public string Name { get; }
    public long Count { get; set; }
    public long TotalDuration { get; set; }
    public long MinDuration { get; set; } = long.MaxValue;
    public long MaxDuration { get; set; }
    public double TotalValue { get; set; }
    public double MinValue { get; set; } = double.MaxValue;
    public double MaxValue { get; set; } = double.MinValue;
    public List<long> Durations { get; } = new();
    public DateTime LastUpdated { get; set; }
    public object Lock { get; } = new();

    public MetricData(string name)
    {
        Name = name;
        LastUpdated = DateTime.UtcNow;
    }
}

internal class ErrorData
{
    public string Operation { get; }
    public string ErrorType { get; }
    public long Count { get; set; }
    public DateTime LastOccurrence { get; set; }
    public object Lock { get; } = new();

    public ErrorData(string operation, string errorType)
    {
        Operation = operation;
        ErrorType = errorType;
        LastOccurrence = DateTime.UtcNow;
    }
}

public class MetricsSummary
{
    public DateTime GeneratedAt { get; set; }
    public List<MetricInfo> Metrics { get; set; } = new();
    public List<ErrorInfo> Errors { get; set; } = new();

    public string ToFormattedString()
    {
        var lines = new List<string>
        {
            "=".PadRight(80, '='),
            $"Metrics Summary - Generated at {GeneratedAt:yyyy-MM-dd HH:mm:ss}",
            "=".PadRight(80, '='),
            ""
        };

        if (Metrics.Any())
        {
            lines.Add("PERFORMANCE METRICS:");
            lines.Add("-".PadRight(80, '-'));
            lines.Add($"{"Operation",-30} {"Count",8} {"Avg",10} {"P50",10} {"P95",10} {"P99",10}");
            lines.Add("-".PadRight(80, '-'));

            foreach (var metric in Metrics.OrderByDescending(m => m.Count))
            {
                lines.Add($"{metric.Name,-30} {metric.Count,8} {metric.AvgDuration,10:F0}ms {metric.P50Duration,10}ms {metric.P95Duration,10}ms {metric.P99Duration,10}ms");
            }

            lines.Add("");
        }

        if (Errors.Any())
        {
            lines.Add("ERRORS:");
            lines.Add("-".PadRight(80, '-'));
            lines.Add($"{"Operation",-30} {"Error Type",-25} {"Count",8} {"Last"}");
            lines.Add("-".PadRight(80, '-'));

            foreach (var error in Errors.OrderByDescending(e => e.Count))
            {
                lines.Add($"{error.Operation,-30} {error.ErrorType,-25} {error.Count,8} {error.LastOccurrence:HH:mm:ss}");
            }

            lines.Add("");
        }

        lines.Add("=".PadRight(80, '='));

        return string.Join(Environment.NewLine, lines);
    }
}

public class MetricInfo
{
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
    public double AvgValue { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public long AvgDuration { get; set; }
    public long MinDuration { get; set; }
    public long MaxDuration { get; set; }
    public long P50Duration { get; set; }
    public long P95Duration { get; set; }
    public long P99Duration { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ErrorInfo
{
    public string Operation { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime LastOccurrence { get; set; }
}
