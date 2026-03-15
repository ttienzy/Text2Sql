namespace TextToSqlAgent.Application.Pipelines;

using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Request model for medium query pipeline
/// Used for queries with JOINs, aggregation, time filters, ranking
/// </summary>
public class MediumQueryRequest
{
    /// <summary>
    /// User's natural language query in Vietnamese
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Database connection identifier
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// User identifier for tracking
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Conversation identifier for context
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Maximum number of rows to return
    /// </summary>
    public int MaxRows { get; set; } = 100;

    /// <summary>
    /// Whether to use LLM for result formatting
    /// If false, uses template-based formatting (faster)
    /// </summary>
    public bool UseLlmFormatting { get; set; } = false;

    /// <summary>
    /// Ambiguity score from classifier (0-1)
    /// Higher scores indicate more ambiguous queries
    /// </summary>
    public double AmbiguityScore { get; set; } = 0;

    /// <summary>
    /// Query complexity classification
    /// </summary>
    public Routing.QueryComplexity Complexity { get; set; } = Routing.QueryComplexity.Medium;
}
