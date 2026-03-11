using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Evaluation.Models;

/// <summary>
/// Result of evaluating a single example
/// P1-08: Enhanced with proper result validation
/// </summary>
public class EvaluationResult
{
    public EvaluationExample Example { get; set; } = null!;
    public AgentResponse? AgentResponse { get; set; }

    /// <summary>
    /// Whether the query executed without errors (does NOT mean correct results)
    /// </summary>
    public bool ExecutionSuccess { get; set; }

    /// <summary>
    /// Whether the generated SQL exactly matches the ground truth SQL
    /// </summary>
    public bool ExactMatch { get; set; }

    /// <summary>
    /// P1-08: Whether the actual query results match the expected results
    /// This is the TRUE measure of correctness
    /// </summary>
    public bool ResultMatch { get; set; }

    /// <summary>
    /// P1-08: Result similarity score (0-100) for partial credit
    /// </summary>
    public double ResultSimilarity { get; set; }

    public double SchemaLinkingPrecision { get; set; }
    public double SchemaLinkingRecall { get; set; }
    public long LatencyMs { get; set; }
    public int TokensUsed { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// P1-08: Detailed validation failure reason
    /// </summary>
    public string? ValidationFailureReason { get; set; }

    public static EvaluationResult Failed(EvaluationExample example, Exception ex)
    {
        return new EvaluationResult
        {
            Example = example,
            ExecutionSuccess = false,
            ErrorMessage = ex.Message,
            ValidationFailureReason = $"Exception: {ex.Message}"
        };
    }
}
