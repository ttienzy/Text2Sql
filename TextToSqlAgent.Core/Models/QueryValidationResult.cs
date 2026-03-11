using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Result of query validation to determine if query is database-related
/// </summary>
public class QueryValidationResult
{
    /// <summary>
    /// Whether the query is relevant to database operations
    /// </summary>
    public bool IsRelevant { get; set; }

    /// <summary>
    /// Confidence score (0.0 to 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Type of query detected
    /// </summary>
    public QueryType QueryType { get; set; }

    /// <summary>
    /// Reason for classification
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Suggested response for out-of-scope queries
    /// </summary>
    public string? SuggestedResponse { get; set; }

    /// <summary>
    /// Whether clarification is needed
    /// </summary>
    public bool NeedsClarification { get; set; }

    /// <summary>
    /// Clarification question to ask user
    /// </summary>
    public string? ClarificationQuestion { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueryType
{
    /// <summary>
    /// Database query (SELECT, aggregate, etc.)
    /// </summary>
    DatabaseQuery,

    /// <summary>
    /// Schema/metadata query
    /// </summary>
    SchemaQuery,

    /// <summary>
    /// General conversation (greetings, thanks, etc.)
    /// </summary>
    Conversation,

    /// <summary>
    /// Out of scope (weather, news, etc.)
    /// </summary>
    OutOfScope,

    /// <summary>
    /// Ambiguous - needs clarification
    /// </summary>
    Ambiguous
}
