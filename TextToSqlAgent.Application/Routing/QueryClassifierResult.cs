namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Result of query classification with confidence score
/// </summary>
public class QueryClassifierResult
{
    /// <summary>
    /// The classified complexity tier
    /// </summary>
    public QueryComplexity Complexity { get; set; }

    /// <summary>
    /// Confidence score between 0 and 1
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Human-readable reasoning for the classification
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Whether LLM was used for classification
    /// </summary>
    public bool UsedLLM { get; set; }

    /// <summary>
    /// Keywords that triggered the classification
    /// </summary>
    public List<string> MatchedKeywords { get; set; } = new();

    /// <summary>
    /// Classification method used
    /// </summary>
    public ClassificationMethod Method { get; set; }
}

/// <summary>
/// Method used for classification
/// </summary>
public enum ClassificationMethod
{
    /// <summary>
    /// Rule-based keyword matching (fast, no LLM)
    /// </summary>
    RuleBased,

    /// <summary>
    /// LLM-based classification (slower, more accurate for ambiguous cases)
    /// </summary>
    LLMBased
}
