using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipeline;

/// <summary>
/// Represents a single processing stage in the query pipeline.
/// Each stage performs one well-defined operation and can short-circuit the pipeline.
/// </summary>
public interface IPipelineStage
{
    /// <summary>Human-readable name for logging and progress reporting.</summary>
    string StageName { get; }

    /// <summary>The AgentStage enum value for SSE progress reporting.</summary>
    AgentStage Stage { get; }

    /// <summary>Progress value (0.0–1.0) when this stage starts.</summary>
    double ProgressStart { get; }

    /// <summary>
    /// Execute this stage, mutating the shared PipelineContext.
    /// Return a StageResult indicating whether the pipeline should continue or stop.
    /// </summary>
    Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct);
}
