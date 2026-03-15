namespace TextToSqlAgent.Application.Pipelines;

using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Interface for Simple Query Pipeline
/// Handles 70% of queries that are simple - single table queries without joins, aggregation, or complex filters
/// Target: 3-5 seconds with 2-3 LLM calls
/// </summary>
public interface ISimpleQueryPipeline
{
    /// <summary>
    /// Execute the simple query pipeline
    /// </summary>
    /// <param name="request">Query request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Query result with SQL and formatted answer</returns>
    Task<QueryResult> ExecuteAsync(SimpleQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if this pipeline can handle the given query
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="complexity">Query complexity classification</param>
    /// <returns>True if this pipeline can handle the query</returns>
    bool CanHandle(string query, QueryComplexity complexity);
}
