namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// LLM-based query classifier for ambiguous cases
/// Uses lightweight model (gpt-4o-mini or similar)
/// </summary>
public interface ILLMQueryClassifier
{
    /// <summary>
    /// Classify query complexity using LLM
    /// </summary>
    /// <param name="query">User query in Vietnamese</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Classification result with complexity and confidence</returns>
    Task<QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default);
}
