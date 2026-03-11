using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// LLM-based reflection engine for evaluating progress
/// </summary>
public class ReflectionEngine : IReflectionEngine
{
    private readonly ILLMClient _llm;
    private readonly ILogger<ReflectionEngine> _logger;

    public ReflectionEngine(
        ILLMClient llm,
        ILogger<ReflectionEngine> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentReflection> ReflectAsync(
        AgentContext context,
        AgentObservation observation,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[ReflectionEngine] Reflecting on observation");

        var prompt = BuildReflectionPrompt(context, observation);
        var response = await _llm.CompleteAsync(prompt, ct);

        var reflection = ParseReflectionResponse(response);

        _logger.LogInformation("[ReflectionEngine] Assessment: {Assessment}, ShouldTerminate: {Terminate}",
            reflection.Assessment, reflection.ShouldTerminate);

        return reflection;
    }

    private string BuildReflectionPrompt(AgentContext context, AgentObservation observation)
    {
        var lastStep = context.State.History.LastOrDefault();

        var prompt = $@"You are evaluating the progress of a Text-to-SQL agent.

# ORIGINAL QUESTION
{context.Request.Question}

# LAST ACTION
Tool: {lastStep?.Action?.ToolName}
Parameters: {System.Text.Json.JsonSerializer.Serialize(lastStep?.Action?.Parameters)}

# OBSERVATION
Success: {observation.Success}
Result: {observation.Result}
Error: {observation.ErrorMessage}

# HISTORY
{context.GetHistorySummary()}

# YOUR TASK
Evaluate the observation and decide:
1. Did the action succeed?
2. Are we making progress toward answering the question?
3. Should we continue or terminate?
4. If continuing, what should we do next?

Respond in this format:
ASSESSMENT: <your evaluation of the current state>
SHOULD_TERMINATE: <YES or NO>
TERMINATION_REASON: <if YES, why are we done?>
NEXT_ACTION: <if NO, what should we do next?>
CONFIDENCE: <0.0 to 1.0, how confident are you?>
";

        return prompt;
    }

    private AgentReflection ParseReflectionResponse(string response)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var reflection = new AgentReflection();

        foreach (var line in lines)
        {
            if (line.StartsWith("ASSESSMENT:", StringComparison.OrdinalIgnoreCase))
            {
                reflection.Assessment = line.Substring("ASSESSMENT:".Length).Trim();
            }
            else if (line.StartsWith("SHOULD_TERMINATE:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring("SHOULD_TERMINATE:".Length).Trim();
                reflection.ShouldTerminate = value.Equals("YES", StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("TERMINATION_REASON:", StringComparison.OrdinalIgnoreCase))
            {
                reflection.TerminationReason = line.Substring("TERMINATION_REASON:".Length).Trim();
            }
            else if (line.StartsWith("NEXT_ACTION:", StringComparison.OrdinalIgnoreCase))
            {
                reflection.NextAction = line.Substring("NEXT_ACTION:".Length).Trim();
            }
            else if (line.StartsWith("CONFIDENCE:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Substring("CONFIDENCE:".Length).Trim();
                if (double.TryParse(value, out var confidence))
                {
                    reflection.Confidence = confidence;
                }
            }
        }

        // Defaults
        if (string.IsNullOrEmpty(reflection.Assessment))
        {
            reflection.Assessment = "Continuing execution";
        }

        return reflection;
    }
}
