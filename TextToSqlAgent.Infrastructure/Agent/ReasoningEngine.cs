using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// LLM-based reasoning engine for agent planning
/// </summary>
public class ReasoningEngine : IReasoningEngine
{
    private readonly ILLMClient _llm;
    private readonly IToolRegistry _toolRegistry;
    private readonly PromptRegistry _promptRegistry;
    private readonly ILogger<ReasoningEngine> _logger;

    public ReasoningEngine(
        ILLMClient llm,
        IToolRegistry toolRegistry,
        PromptRegistry promptRegistry,
        ILogger<ReasoningEngine> logger)
    {
        _llm = llm;
        _toolRegistry = toolRegistry;
        _promptRegistry = promptRegistry;
        _logger = logger;
    }

    public async Task<(string Thought, string Plan)> ThinkAsync(
        AgentContext context,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[ReasoningEngine] Generating thought for step {Step}", context.Steps + 1);

        var prompt = BuildReasoningPrompt(context);
        var response = await _llm.CompleteAsync(prompt, ct);

        // Parse response using structured JSON contract
        var (thought, plan) = ParseReasoningResponse(response);

        _logger.LogInformation("[ReasoningEngine] Thought: {Thought}", thought);
        _logger.LogDebug("[ReasoningEngine] Plan: {Plan}", plan);

        return (thought, plan);
    }

    private string BuildReasoningPrompt(AgentContext context)
    {
        try
        {
            // Use centralized prompt template
            var variables = new Dictionary<string, object>
            {
                ["question"] = context.Request.Question,
                ["database_name"] = context.Request.DatabaseId ?? "Unknown",
                ["current_step"] = context.Steps + 1,
                ["max_steps"] = 10,
                ["previous_steps"] = context.GetHistorySummary(),
                ["available_tools"] = _toolRegistry.GetToolDescriptions()
            };

            return _promptRegistry.GetComposedPrompt("agent_reasoning", new List<string>(), variables);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load prompt template, using fallback");

            // Fallback to hardcoded prompt
            return $@"You are an intelligent Text-to-SQL agent. Your goal is to answer the user's question by using available tools.

# QUESTION
{context.Request.Question}

# AVAILABLE TOOLS
{_toolRegistry.GetToolDescriptions()}

# HISTORY
{context.GetHistorySummary()}

# YOUR TASK
Think step-by-step about what to do next.

Respond in JSON only:
{{
  ""thought"": ""<your reasoning>"",
  ""plan"": ""<specific next step plan>""
}}";
        }
    }

    private (string Thought, string Plan) ParseReasoningResponse(string response)
    {
        var cleaned = response
            .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var jsonReasoning = TryParseJsonReasoning(cleaned);
        if (jsonReasoning != null)
        {
            return jsonReasoning.Value;
        }

        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string thought = "";
        string plan = "";

        foreach (var line in lines)
        {
            if (line.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase))
            {
                thought = line.Substring("THOUGHT:".Length).Trim();
            }
            else if (line.StartsWith("PLAN:", StringComparison.OrdinalIgnoreCase))
            {
                plan = line.Substring("PLAN:".Length).Trim();
            }
        }

        // Fallback if format not followed
        if (string.IsNullOrEmpty(thought))
        {
            thought = response.Split('\n').FirstOrDefault() ?? "Continuing execution";
        }

        if (string.IsNullOrEmpty(plan))
        {
            plan = cleaned;
        }

        return (thought, plan);
    }

    private static (string Thought, string Plan)? TryParseJsonReasoning(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = doc.RootElement;
            var thought = GetStringProperty(root, "thought");
            var plan = GetStringProperty(root, "plan");

            if (string.IsNullOrWhiteSpace(plan))
            {
                plan = GetStringProperty(root, "next_action");
            }

            if (string.IsNullOrWhiteSpace(plan))
            {
                plan = GetStringProperty(root, "action");
            }

            if (string.IsNullOrWhiteSpace(thought) && string.IsNullOrWhiteSpace(plan))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(thought))
            {
                thought = "Continuing execution";
            }

            if (string.IsNullOrWhiteSpace(plan))
            {
                plan = "Gather more context before taking next action";
            }

            return (thought, plan);
        }
        catch
        {
            return null;
        }
    }

    private static string GetStringProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }
}
