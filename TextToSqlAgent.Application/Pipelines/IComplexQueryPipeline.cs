namespace TextToSqlAgent.Application.Pipelines;

using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Interface for Complex Query Pipeline
/// Handles 5% of queries that require subqueries, trend analysis, comparisons, ambiguous queries
/// Target: 30-60 seconds with ReAct agent (optimized for fewer LLM calls)
/// </summary>
public interface IComplexQueryPipeline
{
    /// <summary>
    /// Execute the complex query pipeline
    /// </summary>
    /// <param name="request">Query request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Query result with SQL and formatted answer</returns>
    Task<QueryResult> ExecuteAsync(ComplexQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if this pipeline can handle the given query
    /// </summary>
    /// <param name="complexity">Query complexity classification</param>
    /// <returns>True if this pipeline can handle the query</returns>
    bool CanHandle(QueryComplexity complexity);
}

/// <summary>
/// Request model for complex query pipeline
/// Used for subqueries, trend analysis, comparisons, ambiguous queries
/// </summary>
public class ComplexQueryRequest
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
    /// </summary>
    public bool UseLlmFormatting { get; set; } = true;

    /// <summary>
    /// Ambiguity score from classifier (0-1)
    /// </summary>
    public double AmbiguityScore { get; set; } = 0;

    /// <summary>
    /// Query complexity classification
    /// </summary>
    public Routing.QueryComplexity Complexity { get; set; } = Routing.QueryComplexity.Complex;
}
