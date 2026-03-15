namespace TextToSqlAgent.Application.Pipelines;

using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Request model for simple query pipeline
/// Used for single-table queries without joins or aggregation
/// </summary>
public class SimpleQueryRequest
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
}

/// <summary>
/// Request from API controller
/// </summary>
public class QueryRequest
{
    public string Question { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string? ConversationId { get; set; }
    public QueryRequestOptions? Options { get; set; }
}

public class QueryRequestOptions
{
    public int? MaxRows { get; set; }
    public bool? UseLlmFormatting { get; set; }
}
