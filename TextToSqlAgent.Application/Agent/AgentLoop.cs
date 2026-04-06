using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Agent;

/// <summary>
/// ReAct-style reasoning loop for complex queries.
/// Iterates: Think → Act (tool call) → Observe → Reflect until resolved or max steps reached.
///
/// For simple queries, the existing PipelineOrchestrator is faster and sufficient.
/// AgentLoop is used when complex reasoning is needed (multi-step, ambiguous, decomposition).
/// </summary>
public class AgentLoop
{
    private readonly IEnumerable<IAgentTool> _tools;
    private readonly ILLMClient _llmClient;
    private readonly ILogger<AgentLoop> _logger;
    private readonly int _maxSteps;

    public AgentLoop(
        IEnumerable<IAgentTool> tools,
        ILLMClient llmClient,
        ILogger<AgentLoop> logger,
        int maxSteps = 5)
    {
        _tools = tools;
        _llmClient = llmClient;
        _logger = logger;
        _maxSteps = maxSteps;
    }

    /// <summary>
    /// Execute the agent loop for a given question.
    /// Returns an AgentResponse with the final answer and reasoning trace.
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(WorkingMemory memory, CancellationToken ct)
    {
        _logger.LogInformation("[AgentLoop] Starting reasoning for: {Question}", memory.OriginalQuestion);

        var step = 0;
        while (!memory.IsResolved && step < _maxSteps)
        {
            step++;
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("[AgentLoop] === Step {Step}/{Max} ===", step, _maxSteps);
            memory.ReportProgress(AgentStage.AGENT_THINKING, $"Reasoning step {step}...", step / (double)_maxSteps);

            // 1. THINK: Ask LLM to plan next action
            var plan = await PlanNextActionAsync(memory, ct);

            if (plan.IsFinished)
            {
                _logger.LogInformation("[AgentLoop] ✅ Agent decided it's done: {Answer}", plan.FinalAnswer?[..Math.Min(100, plan.FinalAnswer.Length)]);
                memory.FinalAnswer = plan.FinalAnswer;
                memory.IsResolved = true;

                memory.AddStep(new ReasoningStep
                {
                    Thought = plan.Thought,
                    Action = "FINISH",
                    Observation = plan.FinalAnswer
                });
                break;
            }

            // 2. ACT: Execute the selected tool
            var tool = _tools.FirstOrDefault(t => t.Name.Equals(plan.ToolName, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
            {
                _logger.LogWarning("[AgentLoop] Tool not found: {ToolName}, available: [{Tools}]",
                    plan.ToolName, string.Join(", ", _tools.Select(t => t.Name)));

                memory.AddStep(new ReasoningStep
                {
                    Thought = plan.Thought,
                    Action = $"Tried to use tool '{plan.ToolName}' (not found)",
                    Observation = $"Error: Tool '{plan.ToolName}' is not available. Available tools: {string.Join(", ", _tools.Select(t => t.Name))}"
                });
                continue;
            }

            _logger.LogInformation("[AgentLoop] 🔧 Executing tool: {Tool} with input: {Input}",
                tool.Name, plan.ToolInput.Query[..Math.Min(80, plan.ToolInput.Query.Length)]);

            memory.ReportProgress(AgentStage.AGENT_ACTION, $"Using {tool.Name}...",
                (step - 0.5) / _maxSteps, plan.ToolInput.Query);

            ToolResult result;
            try
            {
                result = await tool.ExecuteAsync(plan.ToolInput, memory, ct);
                result.ToolName = tool.Name;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentLoop] Tool {Tool} threw exception", tool.Name);
                result = ToolResult.Fail(ex.Message);
                result.ToolName = tool.Name;
                memory.Errors.Add($"{tool.Name}: {ex.Message}");
            }

            // 3. OBSERVE: Record the result
            var observation = result.Success
                ? result.Output
                : $"Tool failed: {result.Error}";

            memory.AddStep(new ReasoningStep
            {
                Thought = plan.Thought,
                Action = $"{tool.Name}({plan.ToolInput.Query})",
                Observation = observation.Length > 2000 ? observation[..2000] + "..." : observation,
                ToolResult = result
            });

            _logger.LogDebug("[AgentLoop] Observation: {Observation}", observation[..Math.Min(200, observation.Length)]);
        }

        // Build final response
        return BuildResponse(memory);
    }

    /// <summary>
    /// Ask the LLM to plan the next action based on the current working memory.
    /// </summary>
    private async Task<AgentPlan> PlanNextActionAsync(WorkingMemory memory, CancellationToken ct)
    {
        var toolDescriptions = string.Join("\n", _tools.Select(t =>
            $"- {t.Name}: {t.Description}"));

        var prompt = $"""
            You are an AI agent that helps users query databases. You reason step-by-step.

            Available tools:
            {toolDescriptions}
            - FINISH: Use when you have the final answer.

            {memory.BuildReasoningSummary()}

            Based on the above, decide what to do next.
            Respond in EXACTLY this format (no extra text):
            Thought: <your reasoning>
            Action: <tool_name>
            Input: <what to pass to the tool>

            Or if you have the final answer:
            Thought: <your reasoning>
            Action: FINISH
            Answer: <final answer to the user's question>
            """;

        try
        {
            var systemPrompt = "You are a logical AI Agent reasoning system.";
            var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, prompt, ct);
            return ParseAgentPlan(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentLoop] Failed to get plan from LLM");
            // Fallback: just try SQL pipeline
            return new AgentPlan
            {
                Thought = "LLM planning failed, falling back to direct SQL generation",
                ToolName = "SqlGeneration",
                ToolInput = new ToolInput { Query = memory.OriginalQuestion }
            };
        }
    }

    /// <summary>
    /// Parse the LLM's response into a structured AgentPlan.
    /// </summary>
    private static AgentPlan ParseAgentPlan(string response)
    {
        var plan = new AgentPlan();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Thought:", StringComparison.OrdinalIgnoreCase))
                plan.Thought = trimmed["Thought:".Length..].Trim();
            else if (trimmed.StartsWith("Action:", StringComparison.OrdinalIgnoreCase))
                plan.ToolName = trimmed["Action:".Length..].Trim();
            else if (trimmed.StartsWith("Input:", StringComparison.OrdinalIgnoreCase))
                plan.ToolInput = new ToolInput { Query = trimmed["Input:".Length..].Trim() };
            else if (trimmed.StartsWith("Answer:", StringComparison.OrdinalIgnoreCase))
                plan.FinalAnswer = trimmed["Answer:".Length..].Trim();
        }

        if (string.Equals(plan.ToolName, "FINISH", StringComparison.OrdinalIgnoreCase))
        {
            plan.IsFinished = true;
        }

        return plan;
    }

    /// <summary>
    /// Build the final AgentResponse from the working memory.
    /// </summary>
    private AgentResponse BuildResponse(WorkingMemory memory)
    {
        return new AgentResponse
        {
            Success = memory.IsResolved && memory.Errors.Count == 0,
            Answer = memory.FinalAnswer ?? "I was unable to fully resolve your query.",
            SqlGenerated = memory.GeneratedSql,
            QueryResult = memory.ExecutionResult,
            ProcessingSteps = memory.Steps.Select(s =>
                $"[Step {s.StepNumber}] {s.Action} → {s.Observation?[..Math.Min(100, s.Observation?.Length ?? 0)]}"
            ).ToList(),
            Metadata = new Dictionary<string, object>
            {
                ["agentSteps"] = memory.Steps.Count,
                ["isAgentic"] = true,
                ["reasoningTrace"] = memory.Steps.Select(s => new
                {
                    step = s.StepNumber,
                    thought = s.Thought,
                    action = s.Action,
                    observation = s.Observation?[..Math.Min(200, s.Observation?.Length ?? 0)]
                }).ToList()
            }
        };
    }
}

/// <summary>
/// Represents the LLM's plan for the next action.
/// </summary>
internal class AgentPlan
{
    public string? Thought { get; set; }
    public string? ToolName { get; set; }
    public ToolInput ToolInput { get; set; } = new();
    public string? FinalAnswer { get; set; }
    public bool IsFinished { get; set; }
}
