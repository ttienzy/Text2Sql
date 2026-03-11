using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Routes queries to appropriate handlers based on validation results
/// </summary>
public interface IQueryRouter
{
    /// <summary>
    /// Route query based on validation result
    /// </summary>
    Task<QueryRoute> RouteAsync(
        string question,
        QueryValidationResult validation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Query route result
/// </summary>
public class QueryRoute
{
    public QueryRouteType Type { get; set; }
    public string? ToolName { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string? DirectResponse { get; set; }
    public string? Reasoning { get; set; }
}

/// <summary>
/// Type of routing decision
/// </summary>
public enum QueryRouteType
{
    /// <summary>
    /// Use agent with specific tool
    /// </summary>
    UseTool,

    /// <summary>
    /// Return direct response without agent
    /// </summary>
    DirectResponse,

    /// <summary>
    /// Ask for clarification
    /// </summary>
    NeedsClarification,

    /// <summary>
    /// Use full agent pipeline
    /// </summary>
    UseAgent
}
