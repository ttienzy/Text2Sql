namespace TextToSqlAgent.Evaluation.Models;

/// <summary>
/// Comprehensive evaluation report with metrics
/// P1-08: Enhanced with proper result validation metrics
/// </summary>
public class EvaluationReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "baseline";
    public int TotalExamples { get; set; }
    public List<EvaluationResult> Results { get; set; } = new();

    // P1-08: Accuracy Metrics (clarified)
    /// <summary>
    /// Percentage of queries that executed without errors (NOT correctness)
    /// </summary>
    public double ExecutionAccuracy { get; set; }

    /// <summary>
    /// Percentage of queries where generated SQL exactly matches ground truth
    /// </summary>
    public double ExactMatchAccuracy { get; set; }

    /// <summary>
    /// P1-08: Percentage of queries where results match expected results
    /// This is the TRUE accuracy metric
    /// </summary>
    public double ResultAccuracy { get; set; }

    /// <summary>
    /// P1-08: Average result similarity score (0-100) for partial credit
    /// </summary>
    public double AvgResultSimilarity { get; set; }

    // Schema Linking Metrics
    public double AvgSchemaLinkingPrecision { get; set; }
    public double AvgSchemaLinkingRecall { get; set; }
    public double SchemaLinkingF1 { get; set; }

    // Performance Metrics
    public double AvgLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public int TotalTokensUsed { get; set; }
    public double AvgTokensPerQuery { get; set; }

    // By Difficulty
    public Dictionary<string, double> AccuracyByDifficulty { get; set; } = new();

    // P1-08: Result accuracy by difficulty
    public Dictionary<string, double> ResultAccuracyByDifficulty { get; set; } = new();

    // Error Analysis
    public Dictionary<string, int> ErrorTypes { get; set; } = new();
    public List<EvaluationResult> FailedExamples { get; set; } = new();

    // P1-08: Examples that executed but returned wrong results
    public List<EvaluationResult> IncorrectResults { get; set; } = new();
}
