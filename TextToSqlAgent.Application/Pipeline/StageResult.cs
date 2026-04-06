namespace TextToSqlAgent.Application.Pipeline;

/// <summary>
/// Result returned by each pipeline stage to control pipeline flow.
/// </summary>
public class StageResult
{
    /// <summary>True if the pipeline should stop after this stage (early exit).</summary>
    public bool ShouldStop { get; init; }

    /// <summary>Optional reason for stopping the pipeline (for logging).</summary>
    public string? StopReason { get; init; }

    /// <summary>Stage completed successfully — continue to next stage.</summary>
    public static StageResult Continue() => new() { ShouldStop = false };

    /// <summary>Stage completed and pipeline should stop (response is already built).</summary>
    public static StageResult Stop(string reason) => new() { ShouldStop = true, StopReason = reason };
}
