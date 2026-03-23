using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// Redis-based query result cache for pagination using IDistributedCache
/// </summary>
public class RedisQueryResultCache : IQueryResultCache
{
    private readonly IDistributedCache? _distributedCache;
    private readonly ILogger<RedisQueryResultCache> _logger;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(10);

    public RedisQueryResultCache(
        IDistributedCache? distributedCache,
        ILogger<RedisQueryResultCache> logger)
    {
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<string> CacheResultAsync(
        SqlExecutionResult fullResult,
        string connectionId,
        string? conversationId = null,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        var resultId = Guid.NewGuid().ToString("N");
        var key = GetResultKey(resultId);

        try
        {
            var cacheData = new CachedQueryResult
            {
                ResultId = resultId,
                ConnectionId = connectionId,
                ConversationId = conversationId,
                Result = fullResult,
                CachedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(cacheData);
            var cacheTtl = ttl ?? _defaultTtl;

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cacheTtl
            };

            if (_distributedCache != null)
            {
                await _distributedCache.SetStringAsync(key, json, options, ct);
            }

            _logger.LogInformation(
                "[QueryResultCache] Cached result {ResultId} with {RowCount} rows (TTL: {TTL})",
                resultId, fullResult.RowCount, cacheTtl);

            return resultId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResultCache] Failed to cache result");
            throw;
        }
    }

    public async Task<PaginatedQueryResult?> GetPageAsync(
        string resultId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var key = GetResultKey(resultId);

        try
        {
            if (_distributedCache == null)
            {
                _logger.LogWarning("[QueryResultCache] Distributed cache not available");
                return null;
            }

            var json = await _distributedCache.GetStringAsync(key, ct);

            if (string.IsNullOrEmpty(json))
            {
                _logger.LogWarning("[QueryResultCache] Cache miss for result {ResultId}", resultId);
                return null;
            }

            var cacheData = JsonSerializer.Deserialize<CachedQueryResult>(json);
            if (cacheData?.Result == null)
            {
                return null;
            }

            // Calculate pagination
            var skip = (page - 1) * pageSize;
            var pagedRows = cacheData.Result.Rows
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            _logger.LogDebug(
                "[QueryResultCache] Cache hit for result {ResultId}, page {Page}/{TotalPages}",
                resultId, page, (int)Math.Ceiling((double)cacheData.Result.RowCount / pageSize));

            return new PaginatedQueryResult
            {
                Rows = pagedRows,
                TotalRows = cacheData.Result.RowCount,
                Columns = cacheData.Result.Columns,
                CurrentPage = page,
                PageSize = pageSize,
                ResultId = resultId,
                CachedAt = cacheData.CachedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResultCache] Failed to get page from cache");
            return null;
        }
    }

    public async Task<SqlExecutionResult?> GetFullResultAsync(
        string resultId,
        CancellationToken ct = default)
    {
        var key = GetResultKey(resultId);

        try
        {
            if (_distributedCache == null)
            {
                return null;
            }

            var json = await _distributedCache.GetStringAsync(key, ct);

            if (string.IsNullOrEmpty(json))
            {
                _logger.LogWarning("[QueryResultCache] Cache miss for full result {ResultId}", resultId);
                return null;
            }

            var cacheData = JsonSerializer.Deserialize<CachedQueryResult>(json);
            return cacheData?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResultCache] Failed to get full result from cache");
            return null;
        }
    }

    public async Task DeleteAsync(string resultId, CancellationToken ct = default)
    {
        var key = GetResultKey(resultId);

        try
        {
            if (_distributedCache != null)
            {
                await _distributedCache.RemoveAsync(key, ct);
                _logger.LogDebug("[QueryResultCache] Deleted cached result {ResultId}", resultId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryResultCache] Failed to delete cached result");
        }
    }

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        // Statistics not easily available with IDistributedCache
        // Would need separate counter keys
        return Task.FromResult(new CacheStatistics
        {
            TotalCached = 0,
            TotalHits = 0,
            TotalMisses = 0
        });
    }

    private static string GetResultKey(string resultId) => $"query:result:{resultId}";

    private class CachedQueryResult
    {
        public string ResultId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
        public SqlExecutionResult Result { get; set; } = null!;
        public DateTime CachedAt { get; set; }
    }
}
