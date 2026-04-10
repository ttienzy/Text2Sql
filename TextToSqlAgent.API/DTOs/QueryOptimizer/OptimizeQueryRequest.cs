namespace TextToSqlAgent.API.DTOs.QueryOptimizer;

/// <summary>
/// Request to optimize SQL query
/// </summary>
public class OptimizeQueryRequest
{
    /// <summary>
    /// SQL query to optimize
    /// </summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>
    /// Connection ID for schema context
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Whether to include execution plan comparison
    /// </summary>
    public bool IncludeExecutionPlan { get; set; }
}
