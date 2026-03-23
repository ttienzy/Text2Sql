using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Cache for query results to support pagination
/// </summary>
public interface IQueryResultCache
{
    /// <summary>
    /// Cache full query result and return result ID
    /// </summary>
    Task<string> CacheResultAsync(
        SqlExecutionResult fullResult,
        string connectionId,
        string? conversationId = null,
        TimeSpan? ttl = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get paginated result from cache
    /// </summary>
    Task<PaginatedQueryResult?> GetPageAsync(
        string resultId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Get full result from cache (for export)
    /// </summary>
    Task<SqlExecutionResult?> GetFullResultAsync(
        string resultId,
        CancellationToken ct = default);

    /// <summary>
    /// Delete cached result
    /// </summary>
    Task DeleteAsync(string resultId, CancellationToken ct = default);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken ct = default);
}

public class CacheStatistics
{
    public long TotalCached { get; set; }
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public double HitRate => TotalHits + TotalMisses > 0
        ? (double)TotalHits / (TotalHits + TotalMisses)
        : 0;
}
