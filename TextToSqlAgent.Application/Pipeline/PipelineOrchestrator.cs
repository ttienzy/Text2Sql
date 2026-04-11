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
    /// Execute all registered stages with optimized parallel execution where possible.
    /// PHASE-2 TASK 2.3: Parallelize independent stages to reduce latency.
    /// PHASE-0: Added total pipeline performance measurement.
    /// Returns the final AgentResponse from the PipelineContext.
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var orderedStages = _stages.OrderBy(s => s.ProgressStart).ToList();

        _logger.LogDebug(
            "[Pipeline] Starting optimized pipeline with {Count} stages: [{Stages}]",
            orderedStages.Count,
            string.Join(" → ", orderedStages.Select(s => s.StageName)));

        // ✅ PHASE-0: Start total pipeline measurement
        var pipelineStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // ✅ PHASE-2 TASK 2.3: Execute stages in optimized groups
            await ExecuteStageGroupsAsync(orderedStages, context, ct);
        }
        catch (OperationCanceledException)
        {
            pipelineStopwatch.Stop();
            _logger.LogInformation("[Pipeline] Pipeline cancelled after {Duration}ms", pipelineStopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            pipelineStopwatch.Stop();
            _logger.LogError(ex, "[Pipeline] Pipeline execution failed after {Duration}ms: {Message}",
                pipelineStopwatch.ElapsedMilliseconds, ex.Message);
            context.ReportProgress(AgentStage.ERROR, "Pipeline error", 0.0, ex.Message);

            context.Response.Success = false;
            context.Response.ErrorMessage = $"Pipeline error: {ex.Message}";
            context.Response.ProcessingSteps = context.Steps;
        }

        pipelineStopwatch.Stop();

        // Emit completed if response is successful
        if (context.Response.Success)
        {
            context.ReportProgress(AgentStage.COMPLETED, "Processing complete!", 1.0);
        }

        context.Response.ProcessingSteps = context.Steps;

        // ✅ PHASE-0: Log total pipeline summary (INFO level for visibility)
        _logger.LogInformation(
            "[PERF-SUMMARY] Duration={Duration}ms, Success={Success}",
            pipelineStopwatch.ElapsedMilliseconds,
            context.Response.Success);

        return context.Response;
    }

    /// <summary>
    /// PHASE-2 TASK 2.3: Execute stages in optimized groups with parallelization.
    /// 
    /// Group A (Parallel): IntentClassification + Validation
    /// Group B (Sequential): AgentReasoning → SchemaRetrieval
    /// Group C (Sequential): SqlGeneration → SqlExecution
    /// Group D (Sequential): ResponseFormatting
    /// </summary>
    private async Task ExecuteStageGroupsAsync(
        List<IPipelineStage> orderedStages,
        PipelineContext context,
        CancellationToken ct)
    {
        // Group stages by execution strategy
        var intentStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.CLASSIFYING);
        var validationStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.VALIDATING);
        var reasoningStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.AGENT_THINKING);
        var schemaStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.SCHEMA_RETRIEVAL);
        var sqlGenStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.SQL_GENERATION);
        var sqlExecStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.EXECUTING);
        var responseStage = orderedStages.FirstOrDefault(s => s.Stage == AgentStage.BUILDING_RESPONSE);

        // ✅ GROUP A: Sequential execution (Validation -> IntentClassification)
        // Validation now provides Context Enrichment which IntentClassification needs
        _logger.LogDebug("[Pipeline] → GROUP A: Sequential execution (Validation → IntentClassification)");

        if (validationStage != null)
        {
            var result = await ExecuteStageAsync(validationStage, context, ct);
            if (result.ShouldStop)
            {
                _logger.LogInformation("[Pipeline] ⏹ Pipeline stopped at Validation: {Reason}", result.StopReason);
                return;
            }
        }

        if (intentStage != null)
        {
            var result = await ExecuteStageAsync(intentStage, context, ct);
            if (result.ShouldStop)
            {
                _logger.LogInformation("[Pipeline] ⏹ Pipeline stopped at IntentClassification: {Reason}", result.StopReason);
                return;
            }
        }

        // ✅ GROUP B: Sequential execution (Reasoning → Schema)
        // Reasoning may be skipped, Schema depends on validation
        _logger.LogDebug("[Pipeline] → GROUP B: Sequential execution (Reasoning → Schema)");

        if (reasoningStage != null)
        {
            var result = await ExecuteStageAsync(reasoningStage, context, ct);
            if (result.ShouldStop)
            {
                _logger.LogInformation("[Pipeline] ⏹ Pipeline stopped at Reasoning: {Reason}", result.StopReason);
                return;
            }
        }

        if (schemaStage != null)
        {
            var result = await ExecuteStageAsync(schemaStage, context, ct);
            if (result.ShouldStop)
            {
                _logger.LogInformation("[Pipeline] ⏹ Pipeline stopped at Schema: {Reason}", result.StopReason);
                return;
            }
        }

        // ✅ GROUP C: Sequential execution (SqlGen → SqlExec)
        // SQL execution depends on SQL generation
        _logger.LogDebug("[Pipeline] → GROUP C: Sequential execution (SqlGen → SqlExec)");

        if (sqlGenStage != null)
        {
            var result = await ExecuteStageAsync(sqlGenStage, context, ct);
            if (result.ShouldStop)
            {
                _logger.LogInformation("[Pipeline] ⏹ Pipeline stopped at SqlGen: {Reason}", result.StopReason);
                return;
            }
        }

        if (sqlExecStage != null)
        {
            var result = await ExecuteStageAsync(sqlExecStage, context, ct);
            if (result.ShouldStop)
            {
                _logger.LogInformation("[Pipeline] ⏹ Pipeline stopped at SqlExec: {Reason}", result.StopReason);
                return;
            }
        }

        // ✅ GROUP D: Sequential execution (Response Formatting)
        _logger.LogDebug("[Pipeline] → GROUP D: Response formatting");

        if (responseStage != null)
        {
            await ExecuteStageAsync(responseStage, context, ct);
        }
    }

    /// <summary>
    /// Execute a single stage with error handling and progress reporting.
    /// PHASE-0: Added performance baseline logging.
    /// </summary>
    private async Task<StageResult> ExecuteStageAsync(
        IPipelineStage stage,
        PipelineContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogDebug(
            "[Pipeline] ▶ Executing stage: {Stage} (progress: {Progress:P0})",
            stage.StageName, stage.ProgressStart);

        // Emit SSE progress event before each stage
        context.ReportProgress(stage.Stage, $"Processing: {stage.StageName}...", stage.ProgressStart);

        // ✅ PHASE-0: Start performance measurement
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool llmCalled = false;
        bool cacheHit = false;

        try
        {
            var result = await stage.ExecuteAsync(context, ct);
            stopwatch.Stop();

            // ✅ PHASE-0: Extract metrics from context metadata
            if (context.Response.Metadata != null)
            {
                if (context.Response.Metadata.TryGetValue("llmCalled", out var llmCalledObj))
                {
                    llmCalled = llmCalledObj is bool b && b;
                }
                if (context.Response.Metadata.TryGetValue("cacheHit", out var cacheHitObj))
                {
                    cacheHit = cacheHitObj is bool c && c;
                }
            }

            // ✅ PHASE-0: Baseline performance logging (reduced verbosity)
            _logger.LogDebug(
                "[PERF-BASELINE] Stage={Stage}, Duration={Duration}ms, LLMCalled={LLMCalled}, CacheHit={CacheHit}, Success={Success}",
                stage.StageName,
                stopwatch.ElapsedMilliseconds,
                llmCalled,
                cacheHit,
                !result.ShouldStop);

            if (result.ShouldStop)
            {
                _logger.LogInformation(
                    "[Pipeline] ⏹ Stage {Stage} stopped pipeline: {Reason}",
                    stage.StageName, result.StopReason);
            }
            else
            {
                _logger.LogDebug("[Pipeline] ✓ Stage {Stage} completed", stage.StageName);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // ✅ PHASE-0: Log failure with duration
            _logger.LogError(ex,
                "[PERF-BASELINE] Stage={Stage}, Duration={Duration}ms, Failed=true, Error={Error}",
                stage.StageName,
                stopwatch.ElapsedMilliseconds,
                ex.Message);

            // Emit error progress
            context.ReportProgress(AgentStage.ERROR, $"Error in {stage.StageName}", 0.0, ex.Message);

            // Build error response
            context.Response.Success = false;
            context.Response.ErrorMessage = FormatStageError(stage, ex);
            context.Response.ProcessingSteps = context.Steps;

            return StageResult.Stop($"Stage {stage.StageName} failed: {ex.Message}");
        }
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
