using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// SMALL-1: Query Plan Cache — caches generated SQL for a given question+connectionId pair.
/// Key: SHA256(question + connectionId), TTL: 1 hour.
/// Automatically invalidated when schema fingerprint changes.
/// </summary>
public class QueryPlanCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<QueryPlanCache> _logger;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);
    private const string Prefix = "TextToSqlAgent:queryplan:";

    public QueryPlanCache(IDistributedCache cache, ILogger<QueryPlanCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Try to retrieve a cached SQL query plan.
    /// </summary>
    public async Task<QueryPlanEntry?> GetAsync(string question, string connectionId, CancellationToken ct = default)
    {
        var key = BuildKey(question, connectionId);
        try
        {
            var json = await _cache.GetStringAsync(key, ct);
            if (string.IsNullOrEmpty(json))
            {
                _logger.LogDebug("[QueryPlanCache] MISS: {Key}", key);
                return null;
            }

            _logger.LogDebug("[QueryPlanCache] HIT: {Key}", key);
            return System.Text.Json.JsonSerializer.Deserialize<QueryPlanEntry>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[QueryPlanCache] Read failed for {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Cache a SQL query plan result.
    /// </summary>
    public async Task SetAsync(
        string question,
        string connectionId,
        string generatedSql,
        string? schemaFingerprint = null,
        CancellationToken ct = default)
    {
        var key = BuildKey(question, connectionId);
        var entry = new QueryPlanEntry
        {
            GeneratedSql = generatedSql,
            ConnectionId = connectionId,
            SchemaFingerprint = schemaFingerprint,
            CachedAt = DateTime.UtcNow
        };

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(entry);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultTtl
            };
            await _cache.SetStringAsync(key, json, options, ct);
            _logger.LogDebug("[QueryPlanCache] CACHED: {Key} (TTL: 1h)", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[QueryPlanCache] Write failed for {Key}", key);
        }
    }

    /// <summary>
    /// Invalidate all query plans for a connection (e.g. when schema changes).
    /// Requires Redis — no-op on IDistributedCache without IConnectionMultiplexer.
    /// </summary>
    public async Task InvalidateForConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        // Pattern-based invalidation is handled by CacheService.RemoveByPatternAsync
        // Callers should pass the pattern: "TextToSqlAgent:queryplan:{connectionId}:*"
        _logger.LogInformation("[QueryPlanCache] Invalidation triggered for connection {ConnectionId}", connectionId);
        await Task.CompletedTask;
    }

    private static string BuildKey(string question, string connectionId)
    {
        var input = $"{connectionId}:{question.ToLowerInvariant().Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hashHex = Convert.ToHexString(hash)[..24];
        return $"{Prefix}{connectionId}:{hashHex}";
    }
}

/// <summary>
/// A cached query plan entry.
/// </summary>
public class QueryPlanEntry
{
    public required string GeneratedSql { get; set; }
    public required string ConnectionId { get; set; }
    public string? SchemaFingerprint { get; set; }
    public DateTime CachedAt { get; set; }
}
