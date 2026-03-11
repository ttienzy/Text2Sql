using TextToSqlAgent.Evaluation.Models;

namespace TextToSqlAgent.Evaluation.Metrics;

/// <summary>
/// Calculate evaluation metrics from results
/// P1-08: Enhanced with proper result validation metrics
/// </summary>
public class MetricsCalculator
{
    public EvaluationReport GenerateReport(List<EvaluationResult> results, string version = "baseline")
    {
        var report = new EvaluationReport
        {
            Version = version,
            TotalExamples = results.Count,
            Results = results
        };

        // P1-08: Accuracy Metrics (clarified)
        report.ExecutionAccuracy = CalculateAccuracy(results, r => r.ExecutionSuccess);
        report.ExactMatchAccuracy = CalculateAccuracy(results, r => r.ExactMatch);
        report.ResultAccuracy = CalculateAccuracy(results, r => r.ResultMatch); // TRUE accuracy
        report.AvgResultSimilarity = results.Average(r => r.ResultSimilarity);

        // Schema Linking Metrics
        report.AvgSchemaLinkingPrecision = results.Average(r => r.SchemaLinkingPrecision);
        report.AvgSchemaLinkingRecall = results.Average(r => r.SchemaLinkingRecall);
        report.SchemaLinkingF1 = CalculateF1(report.AvgSchemaLinkingPrecision, report.AvgSchemaLinkingRecall);

        // Performance Metrics
        var latencies = results.Select(r => (double)r.LatencyMs).OrderBy(l => l).ToList();
        report.AvgLatencyMs = latencies.Average();
        report.P50LatencyMs = CalculatePercentile(latencies, 0.50);
        report.P95LatencyMs = CalculatePercentile(latencies, 0.95);
        report.P99LatencyMs = CalculatePercentile(latencies, 0.99);

        report.TotalTokensUsed = results.Sum(r => r.TokensUsed);
        report.AvgTokensPerQuery = results.Average(r => r.TokensUsed);

        // By Difficulty - Execution Accuracy
        var difficulties = results.Select(r => r.Example.Difficulty).Distinct();
        foreach (var difficulty in difficulties)
        {
            var diffResults = results.Where(r => r.Example.Difficulty == difficulty).ToList();
            report.AccuracyByDifficulty[difficulty] = CalculateAccuracy(diffResults, r => r.ExecutionSuccess);

            // P1-08: Result accuracy by difficulty
            report.ResultAccuracyByDifficulty[difficulty] = CalculateAccuracy(diffResults, r => r.ResultMatch);
        }

        // Error Analysis
        report.FailedExamples = results.Where(r => !r.ExecutionSuccess).ToList();

        // P1-08: Examples that executed but returned wrong results
        report.IncorrectResults = results
            .Where(r => r.ExecutionSuccess && !r.ResultMatch)
            .ToList();

        foreach (var failed in report.FailedExamples)
        {
            var errorType = failed.ErrorMessage?.Split(':').FirstOrDefault() ?? "Unknown";
            report.ErrorTypes[errorType] = report.ErrorTypes.GetValueOrDefault(errorType, 0) + 1;
        }

        return report;
    }

    private double CalculateAccuracy(List<EvaluationResult> results, Func<EvaluationResult, bool> predicate)
    {
        if (results.Count == 0) return 0;
        return (double)results.Count(predicate) / results.Count * 100;
    }

    private double CalculateF1(double precision, double recall)
    {
        if (precision + recall == 0) return 0;
        return 2 * (precision * recall) / (precision + recall);
    }

    private double CalculatePercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
    }
}
