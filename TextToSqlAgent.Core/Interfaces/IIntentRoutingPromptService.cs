using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

public interface IIntentRoutingPromptService
{
    Task<string?> GenerateClarificationAsync(
        string originalInput,
        string language,
        IReadOnlyList<string> ambiguityReasons,
        IReadOnlyList<string> existingSuggestions,
        CancellationToken cancellationToken = default);

    Task<string?> GenerateRejectionMessageAsync(
        string originalInput,
        string language,
        IntentCategory intent,
        RiskLevel riskLevel,
        double confidence,
        IReadOnlyList<string> detectedEntities,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken = default);
}
