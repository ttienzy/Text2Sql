using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Agent;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage that intercepts complex queries and delegates them to the ReAct AgentLoop.
/// If the query is simple, it falls through to the standard sequential pipeline.
/// </summary>
public class AgentReasoningStage : IPipelineStage
{
    private readonly AgentLoop _agentLoop;
    private readonly ILogger<AgentReasoningStage> _logger;

    public string StageName => "Agentic Reasoning";
    public AgentStage Stage => AgentStage.AGENT_THINKING;
    public double ProgressStart => 0.35; // ✅ PHASE-2 TASK-08: Changed from 0.20 to 0.35 to avoid conflict with SchemaRetrieval

    public AgentReasoningStage(AgentLoop agentLoop, ILogger<AgentReasoningStage> logger)
    {
        _agentLoop = agentLoop;
        _logger = logger;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // LLM-based complexity routing replaces fragile keyword heuristics
        var complexityScore = context.IntentClassification?.ComplexityScore ?? 0.0;
        bool isComplex = complexityScore >= 0.5;

        // Fallback for missing IntentClassification or low complexity
        if (context.IntentClassification == null)
        {
            _logger.LogWarning("[AgentReasoningStage] Missing IntentClassification, falling back to sequential pipeline");
        }

        if (!isComplex)
        {
            _logger.LogInformation("[AgentReasoningStage] Complexity score is {Score:F2} (< 0.5), falling back to sequential pipeline", complexityScore);
            return StageResult.Continue();
        }

        _logger.LogInformation("[AgentReasoningStage] Complex query detected, switching to AgentLoop");
        context.Steps.Add("Switched to autonomous ReAct agent reasoning");

        var memory = new WorkingMemory
        {
            OriginalQuestion = context.UserQuestion,
            ConversationId = context.ConversationId,
            FullSchema = context.Schema, // pass existing schema if preloaded
            Progress = context.Progress,
            SqlTokenCallback = context.SqlTokenCallback
        };

        var response = await _agentLoop.ExecuteAsync(memory, ct);

        // Map agent results back to pipeline context
        context.Response = response;
        context.GeneratedSql = memory.GeneratedSql;
        context.ExecutionResult = memory.ExecutionResult;

        // Track the agent steps
        context.Steps.AddRange(memory.Steps.Select(s => $"Agent Step {s.StepNumber}: {s.Action}"));

        // AgentLoop replaces Schema+SQL+Exec stages, so we short-circuit the pipeline
        return StageResult.Stop("AgentLoop resolved the query");
    }
}
