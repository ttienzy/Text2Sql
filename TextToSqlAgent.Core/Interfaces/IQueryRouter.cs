namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Routes queries to appropriate handlers based on intent
/// Enables fast-path for greetings/out-of-scope without loading schema
/// </summary>
public interface IQueryRouter
{
    Task<QueryRoute> RouteAsync(
        string question,
        string? conversationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of query routing with metadata
/// </summary>
public class QueryRoute
{
    public RouteType Type { get; set; }
    public string? DirectResponse { get; set; }
    public double Confidence { get; set; }
    public string? Reason { get; set; }
    public bool RequiresSchema { get; set; }
    public bool RequiresLLM { get; set; }
}

/// <summary>
/// Types of query routes
/// </summary>
public enum RouteType
{
    Greeting,           // "hello", "hi" → fast path, no schema/LLM
    OutOfScope,         // "weather", "news" → fast path, no schema/LLM
    Ambiguous,          // "show data" → needs clarification with LLM
    DatabaseQuery,      // "list customers" → full pipeline
    SchemaQuery         // "what tables" → schema only, no SQL execution
}
