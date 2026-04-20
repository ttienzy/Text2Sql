using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Application.Routing;

public class PromptRegistryIntentRoutingPromptService : IIntentRoutingPromptService
{
    private readonly ILLMClient _llmClient;
    private readonly PromptRegistry _promptRegistry;

    public PromptRegistryIntentRoutingPromptService(ILLMClient llmClient, PromptRegistry promptRegistry)
    {
        _llmClient = llmClient;
        _promptRegistry = promptRegistry;
    }

    public async Task<string?> GenerateClarificationAsync(
        string originalInput,
        string language,
        IReadOnlyList<string> ambiguityReasons,
        IReadOnlyList<string> existingSuggestions,
        CancellationToken cancellationToken = default)
    {
        var variables = new Dictionary<string, object>
        {
            ["original_query"] = originalInput,
            ["language"] = language == "vi" ? "Vietnamese" : "English",
            ["ambiguity_reasons"] = string.Join("\n", ambiguityReasons.Select(r => $"- {r}")),
            ["existing_suggestions"] = string.Join("\n", existingSuggestions.Select(s => $"- {s}"))
        };

        var (systemPrompt, userPrompt) = _promptRegistry.GetSystemAndUserPrompts(
            "core/ambiguity-resolution",
            new List<string>(),
            variables,
            includeExamples: false);

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, cancellationToken);
        return response.Trim();
    }

    public async Task<string?> GenerateRejectionMessageAsync(
        string originalInput,
        string language,
        IntentCategory intent,
        RiskLevel riskLevel,
        double confidence,
        IReadOnlyList<string> detectedEntities,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken = default)
    {
        var variables = new Dictionary<string, object>
        {
            ["original_question"] = originalInput,
            ["intent_type"] = intent.ToString(),
            ["risk_level"] = riskLevel.ToString(),
            ["confidence"] = confidence.ToString("0.00"),
            ["detected_entities"] = detectedEntities.ToList(),
            ["warnings"] = warnings.ToList(),
            ["language"] = language == "vi" ? "Vietnamese" : "English"
        };

        var (systemPrompt, userPrompt) = _promptRegistry.GetSystemAndUserPrompts(
            "sql-generation/rejection",
            new List<string>(),
            variables,
            includeExamples: false);

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, cancellationToken);
        return response.Trim();
    }
}
