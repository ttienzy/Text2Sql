namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Interface for query complexity classifier
/// </summary>
public interface IQueryClassifier
{
    /// <summary>
    /// Classify query complexity using two-layer approach
    /// </summary>
    /// <param name="query">User query in Vietnamese</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Classification result with complexity and confidence</returns>
    Task<QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Check if preliminary classification requires LLM fallback
    /// </summary>
    /// <param name="preliminary">Preliminary classification result</param>
    /// <returns>True if LLM classification is needed</returns>
    bool RequiresLLMClassification(QueryClassifierResult preliminary);
}
