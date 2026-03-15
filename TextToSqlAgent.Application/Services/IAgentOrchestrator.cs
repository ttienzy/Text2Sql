namespace TextToSqlAgent.Application.Services;

using TextToSqlAgent.Application.Pipelines;
using TextToSqlAgent.Application.Routing;
using TextToSqlAgent.Core.Models;
using QueryRequest = TextToSqlAgent.Application.Pipelines.QueryRequest;

/// <summary>
/// Core orchestration interface that routes queries to appropriate pipelines
/// based on complexity classification
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Execute query through the appropriate pipeline based on complexity
    /// </summary>
    /// <param name="request">Query request with question and metadata</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Query result with SQL, data, and formatted answer</returns>
    Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Classify query complexity without executing pipeline
    /// </summary>
    /// <param name="query">User query</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Classification result with complexity tier</returns>
    Task<QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Connect to a database and perform automatic schema indexing if needed
    /// </summary>
    /// <param name="connectionId">Unique identifier for the connection</param>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="forceReindex">Force re-indexing regardless of fingerprint comparison</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Connection result with indexing status</returns>
    Task<ConnectionResult> ConnectToDatabaseAsync(
        string connectionId,
        string connectionString,
        bool forceReindex = false,
        CancellationToken ct = default);

}
