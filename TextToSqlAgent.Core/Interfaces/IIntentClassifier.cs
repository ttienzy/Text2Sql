using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Intent classifier that routes queries to appropriate pipelines
/// </summary>
public interface IIntentClassifier
{
    /// <summary>
    /// Classify user question into intent category
    /// </summary>
    /// <param name="question">User's natural language question</param>
    /// <param name="conversationHistory">Optional conversation context for pronoun resolution</param>
    /// <param name="databaseContext">Optional database schema context</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Classification result with intent, route, and confidence</returns>
    Task<IntentClassificationResult> ClassifyAsync(
        string question,
        string? conversationHistory = null,
        string? databaseContext = null,
        CancellationToken ct = default);
}
