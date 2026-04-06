using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipeline;

/// <summary>
/// Thin pipeline runner that executes stages in sequence.
/// Each stage can short-circuit the pipeline via StageResult.Stop().
/// Progress events are emitted before each stage for SSE streaming.
/// </summary>
public class PipelineOrchestrator
{
    private readonly IEnumerable<IPipelineStage> _stages;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IEnumerable<IPipelineStage> stages,
        ILogger<PipelineOrchestrator> logger)
    {
        _stages = stages;
        _logger = logger;
    }

    /// <summary>
    /// Execute all registered stages in order of ProgressStart.
    /// Returns the final AgentResponse from the PipelineContext.
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var orderedStages = _stages.OrderBy(s => s.ProgressStart).ToList();

        _logger.LogInformation(
            "[Pipeline] Starting pipeline with {Count} stages: [{Stages}]",
            orderedStages.Count,
            string.Join(" → ", orderedStages.Select(s => s.StageName)));

        foreach (var stage in orderedStages)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "[Pipeline] ▶ Executing stage: {Stage} (progress: {Progress:P0})",
                stage.StageName, stage.ProgressStart);

            // Emit SSE progress event before each stage
            context.ReportProgress(stage.Stage, $"Processing: {stage.StageName}...", stage.ProgressStart);

            try
            {
                var result = await stage.ExecuteAsync(context, ct);

                if (result.ShouldStop)
                {
                    _logger.LogInformation(
                        "[Pipeline] ⏹ Pipeline stopped at stage {Stage}: {Reason}",
                        stage.StageName, result.StopReason);
                    break;
                }

                _logger.LogDebug("[Pipeline] ✓ Stage {Stage} completed, continuing", stage.StageName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[Pipeline] Pipeline cancelled at stage {Stage}", stage.StageName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Pipeline] ✗ Stage {Stage} failed: {Message}", stage.StageName, ex.Message);

                // Emit error progress
                context.ReportProgress(AgentStage.ERROR, $"Error in {stage.StageName}", 0.0, ex.Message);

                // Build error response
                context.Response.Success = false;
                context.Response.ErrorMessage = FormatStageError(stage, ex);
                context.Response.ProcessingSteps = context.Steps;
                break;
            }
        }

        // Emit completed if response is successful and not already signaled
        if (context.Response.Success)
        {
            context.ReportProgress(AgentStage.COMPLETED, "Processing complete!", 1.0);
        }

        context.Response.ProcessingSteps = context.Steps;
        return context.Response;
    }

    private static string FormatStageError(IPipelineStage stage, Exception ex)
    {
        return ex switch
        {
            HttpRequestException httpEx =>
                $"Network error during {stage.StageName}: {httpEx.Message}\nPlease check your connection.",
            TaskCanceledException =>
                "Request timed out. Please try again or simplify your query.",
            UnauthorizedAccessException =>
                "API key invalid or expired. Please check your configuration.",
            System.Text.Json.JsonException jsonEx =>
                $"Failed to parse AI response during {stage.StageName}: {jsonEx.Message}\nPlease try again.",
            _ =>
                $"Error during {stage.StageName}: {ex.Message}"
        };
    }
}
