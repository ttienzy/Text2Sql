using System.Collections.Concurrent;

namespace TextToSqlAgent.Console.Observability;

/// <summary>
/// Tracks console application metrics for observability
/// </summary>
public class ConsoleMetrics
{
    private readonly ConcurrentDictionary<string, MetricData> _metrics = new();
    private readonly object _lock = new();

    public void RecordQuery(
        bool success,
        TimeSpan processingTime,
        int correctionAttempts,
        int stepsCount)
    {
        lock (_lock)
        {
            var key = "queries";
            if (!_metrics.ContainsKey(key))
            {
                _metrics[key] = new MetricData();
            }

            var metric = _metrics[key];
            metric.TotalCount++;

            if (success)
            {
                metric.SuccessCount++;
            }
            else
            {
                metric.FailureCount++;
            }

            metric.TotalProcessingTime += processingTime;
            metric.TotalCorrectionAttempts += correctionAttempts;
            metric.TotalSteps += stepsCount;

            if (processingTime > metric.MaxProcessingTime)
            {
                metric.MaxProcessingTime = processingTime;
            }

            if (processingTime < metric.MinProcessingTime || metric.MinProcessingTime == TimeSpan.Zero)
            {
                metric.MinProcessingTime = processingTime;
            }
        }
    }

    public MetricsSummary GetSummary()
    {
        lock (_lock)
        {
            if (!_metrics.TryGetValue("queries", out var metric))
            {
                return new MetricsSummary();
            }

            return new MetricsSummary
            {
                TotalQueries = metric.TotalCount,
                SuccessfulQueries = metric.SuccessCount,
                FailedQueries = metric.FailureCount,
                SuccessRate = metric.TotalCount > 0
                    ? (double)metric.SuccessCount / metric.TotalCount
                    : 0,
                AverageProcessingTime = metric.TotalCount > 0
                    ? TimeSpan.FromTicks(metric.TotalProcessingTime.Ticks / metric.TotalCount)
                    : TimeSpan.Zero,
                MaxProcessingTime = metric.MaxProcessingTime,
                MinProcessingTime = metric.MinProcessingTime,
                TotalCorrectionAttempts = metric.TotalCorrectionAttempts,
                AverageCorrectionAttempts = metric.TotalCount > 0
                    ? (double)metric.TotalCorrectionAttempts / metric.TotalCount
                    : 0,
                AverageSteps = metric.TotalCount > 0
                    ? (double)metric.TotalSteps / metric.TotalCount
                    : 0
            };
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _metrics.Clear();
        }
    }

    private class MetricData
    {
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public TimeSpan MaxProcessingTime { get; set; }
        public TimeSpan MinProcessingTime { get; set; }
        public int TotalCorrectionAttempts { get; set; }
        public int TotalSteps { get; set; }
    }
}

public class MetricsSummary
{
    public int TotalQueries { get; set; }
    public int SuccessfulQueries { get; set; }
    public int FailedQueries { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public TimeSpan MaxProcessingTime { get; set; }
    public TimeSpan MinProcessingTime { get; set; }
    public int TotalCorrectionAttempts { get; set; }
    public double AverageCorrectionAttempts { get; set; }
    public double AverageSteps { get; set; }
}
