using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// PHASE-3 TASK 3.1: Redis-based intent cache for distributed caching.
/// Supports pattern-based invalidation and cross-server cache sharing.
/// </summary>
public class RedisIntentCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIntentCache> _logger;
    private readonly IDatabase _db;

    private const string KeyPrefix = "intent:";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public RedisIntentCache(
        IConnectionMultiplexer redis,
        ILogger<RedisIntentCache> logger)
    {
        _redis = redis;
        _logger = logger;
        _db = redis.GetDatabase();
    }

    /// <summary>
    /// Get cached intent classification result.
    /// </summary>
    public async Task<IntentClassificationResult?> GetAsync(string question, string connectionId)
    {
        try
        {
            var key = GenerateCacheKey(question, connectionId);
            var cached = await _db.StringGetAsync(key);

            if (cached.HasValue)
            {
                _logger.LogDebug("[RedisIntentCache] ⚡ HIT for key: {Key}", key.Substring(0, 20));
                return JsonSerializer.Deserialize<IntentClassificationResult>(cached.ToString()!);
            }

            _logger.LogDebug("[RedisIntentCache] MISS for key: {Key}", key.Substring(0, 20));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RedisIntentCache] Failed to get from cache");
            return null; // Graceful degradation
        }
    }

    /// <summary>
    /// Store intent classification result in cache.
    /// </summary>
    public async Task SetAsync(
        string question,
        string connectionId,
        IntentClassificationResult result,
        TimeSpan? ttl = null)
    {
        try
        {
            var key = GenerateCacheKey(question, connectionId);
            var json = JsonSerializer.Serialize(result);

            await _db.StringSetAsync(key, json, ttl ?? DefaultTtl);
            _logger.LogDebug("[RedisIntentCache] SET for key: {Key}, Intent: {Intent}",
                key.Substring(0, 20), result.Intent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RedisIntentCache] Failed to set cache");
            // Don't throw - caching is not critical
        }
    }

    /// <summary>
    /// Get or create intent classification result.
    /// </summary>
    public async Task<IntentClassificationResult> GetOrCreateAsync(
        string question,
        string connectionId,
        Func<Task<IntentClassificationResult>> factory)
    {
        // Try cache first
        var cached = await GetAsync(question, connectionId);
        if (cached != null)
        {
            return cached;
        }

        // Cache miss - call factory
        _logger.LogDebug("[RedisIntentCache] Calling factory for new classification");
        var result = await factory();

        // Store in cache (fire-and-forget)
        _ = SetAsync(question, connectionId, result);

        return result;
    }

    /// <summary>
    /// ✅ NEW: Invalidate all cache entries for a connection.
    /// Useful when connection schema changes.
    /// </summary>
    public async Task InvalidateConnectionAsync(string connectionId)
    {
        try
        {
            var pattern = $"{KeyPrefix}*|{connectionId}";
            await DeleteByPatternAsync(pattern);
            _logger.LogInformation("[RedisIntentCache] Invalidated all cache for connection: {ConnectionId}",
                connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RedisIntentCache] Failed to invalidate connection cache");
        }
    }

    /// <summary>
    /// Clear all cached intents.
    /// </summary>
    public async Task ClearAsync()
    {
        try
        {
            await DeleteByPatternAsync($"{KeyPrefix}*");
            _logger.LogInformation("[RedisIntentCache] Cleared all cache entries");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RedisIntentCache] Failed to clear cache");
        }
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public async Task<CacheStats> GetStatsAsync()
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: $"{KeyPrefix}*").ToList();

            return new CacheStats
            {
                TotalKeys = keys.Count,
                MemoryUsage = await GetMemoryUsageAsync(keys)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RedisIntentCache] Failed to get stats");
            return new CacheStats();
        }
    }

    private async Task DeleteByPatternAsync(string pattern)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern);

        foreach (var key in keys)
        {
            await _db.KeyDeleteAsync(key);
        }
    }

    private async Task<long> GetMemoryUsageAsync(List<RedisKey> keys)
    {
        long total = 0;
        foreach (var key in keys)
        {
            var value = await _db.StringGetAsync(key);
            if (value.HasValue)
            {
                total += value.ToString().Length;
            }
        }
        return total;
    }

    private static string GenerateCacheKey(string question, string connectionId)
    {
        var normalized = question.ToLowerInvariant().Trim();
        var input = $"{normalized}|{connectionId}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return $"{KeyPrefix}{Convert.ToHexString(hash)}";
    }
}

public class CacheStats
{
    public int TotalKeys { get; set; }
    public long MemoryUsage { get; set; }
}
