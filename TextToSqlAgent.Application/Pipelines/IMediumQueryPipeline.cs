namespace TextToSqlAgent.Application.Pipelines;

using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Interface for Medium Query Pipeline
/// Handles 25% of queries that require JOINs, aggregation, time filters, or ranking
/// Target: 15-20 seconds with 5-8 LLM calls
/// </summary>
public interface IMediumQueryPipeline
{
    /// <summary>
    /// Execute the medium query pipeline
    /// </summary>
    /// <param name="request">Query request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Query result with SQL and formatted answer</returns>
    Task<QueryResult> ExecuteAsync(MediumQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if this pipeline can handle the given query
    /// </summary>
    /// <param name="complexity">Query complexity classification</param>
    /// <returns>True if this pipeline can handle the query</returns>
    bool CanHandle(QueryComplexity complexity);
}
