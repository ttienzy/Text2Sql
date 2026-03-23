using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Redis-based cache service for Intent Classification results
/// Caches classification results to reduce LLM calls and improve performance
/// 
/// Key Features:
/// - SHA256 hash of question as cache key
/// - TTL: 1 hour (configurable)
/// - Only caches high confidence results (>= 0.75)
/// - Fallback to memory cache if Redis unavailable
/// </summary>
public class IntentCacheService : IIntentCacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<IntentCacheService> _logger;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);

    // In-memory fallback cache (LRU-like, simple implementation)
    private readonly Dictionary<string, (IntentClassificationResult Result, DateTime CachedAt)> _memoryCache = new();
    private readonly int _maxMemoryCacheSize = 1000;

    private const string KeyPrefix = "TextToSqlAgent:intent:";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public IntentCacheService(
        IConnectionMultiplexer? redis,
        ILogger<IntentCacheService> logger)
    {
        _redis = redis;
        _logger = logger;

        if (_redis != null)
        {
            _logger.LogInformation("[IntentCache] Redis cache ENABLED");
        }
        else
        {
            _logger.LogWarning("[IntentCache] Redis not available, using memory cache only");
        }
    }

    /// <summary>
    /// Get cached intent classification result
    /// </summary>
    public async Task<IntentClassificationResult?> GetCachedAsync(string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return null;

        var hash = ComputeHash(question);

        // Try Redis first
        if (_redis != null)
        {
            try
            {
                var redisResult = await GetFromRedisAsync(hash, ct);
                if (redisResult != null)
                {
                    _logger.LogDebug("[IntentCache] REDIS HIT: {Hash}", hash);
                    return WithCacheMetadata(redisResult, fromCache: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IntentCache] Redis read failed, falling back to memory");
            }
        }

        // Fallback to memory cache
        if (_memoryCache.TryGetValue(hash, out var memResult))
        {
            // Check if expired
            if (DateTime.UtcNow - memResult.CachedAt < _defaultTtl)
            {
                _logger.LogDebug("[IntentCache] MEMORY HIT: {Hash}", hash);
                return WithCacheMetadata(memResult.Result, fromCache: true);
            }
            else
            {
                // Expired, remove
                _memoryCache.Remove(hash);
            }
        }

        _logger.LogDebug("[IntentCache] CACHE MISS: {Hash}", hash);
        return null;
    }

    /// <summary>
    /// Cache intent classification result
    /// Only caches high confidence results to ensure quality
    /// </summary>
    public async Task CacheAsync(string question, IntentClassificationResult result, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return;

        // Only cache high confidence results
        if (result.Confidence < 0.75)
        {
            _logger.LogDebug("[IntentCache] Not caching - confidence {Confidence} < 0.75", result.Confidence);
            return;
        }

        var hash = ComputeHash(question);

        // Save to Redis
        if (_redis != null)
        {
            try
            {
                await SaveToRedisAsync(hash, result, ct);
                _logger.LogDebug("[IntentCache] REDIS CACHED: {Hash} (TTL: {TTL})", hash, _defaultTtl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IntentCache] Redis write failed, using memory cache");
                SaveToMemoryCache(hash, result);
            }
        }
        else
        {
            // Use memory cache only
            SaveToMemoryCache(hash, result);
        }
    }

    /// <summary>
    /// Invalidate cache for a specific question
    /// </summary>
    public async Task InvalidateAsync(string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return;

        var hash = ComputeHash(question);

        // Remove from Redis
        if (_redis != null)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync($"{KeyPrefix}{hash}");
                _logger.LogDebug("[IntentCache] REDIS INVALIDATED: {Hash}", hash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IntentCache] Redis invalidation failed");
            }
        }

        // Remove from memory
        _memoryCache.Remove(hash);
    }

    /// <summary>
    /// Clear all intent caches (useful when schema changes)
    /// </summary>
    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        if (_redis != null)
        {
            try
            {
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                var keys = server.Keys(pattern: $"{KeyPrefix}*").ToArray();

                if (keys.Length > 0)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync(keys);
                    _logger.LogInformation("[IntentCache] Cleared {Count} Redis keys", keys.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IntentCache] Redis clear failed");
            }
        }

        _memoryCache.Clear();
        _logger.LogInformation("[IntentCache] Memory cache cleared");
    }

    #region Private Methods

    private async Task<IntentClassificationResult?> GetFromRedisAsync(string hash, CancellationToken ct)
    {
        if (_redis == null) return null;

        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync($"{KeyPrefix}{hash}");

        if (value.HasValue)
        {
            return JsonSerializer.Deserialize<IntentClassificationResult>(value.ToString(), _jsonOptions);
        }

        return null;
    }

    private async Task SaveToRedisAsync(string hash, IntentClassificationResult result, CancellationToken ct)
    {
        if (_redis == null) return;

        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(result, _jsonOptions);

        await db.StringSetAsync(
            $"{KeyPrefix}{hash}",
            json,
            _defaultTtl);
    }

    private void SaveToMemoryCache(string hash, IntentClassificationResult result)
    {
        // Simple eviction: if full, remove oldest entry
        if (_memoryCache.Count >= _maxMemoryCacheSize)
        {
            var oldestKey = _memoryCache
                .OrderBy(x => x.Value.CachedAt)
                .First()
                .Key;
            _memoryCache.Remove(oldestKey);
            _logger.LogDebug("[IntentCache] Memory cache evicted oldest entry");
        }

        _memoryCache[hash] = (result, DateTime.UtcNow);
        _logger.LogDebug("[IntentCache] MEMORY CACHED: {Hash}", hash);
    }

    private static IntentClassificationResult WithCacheMetadata(
        IntentClassificationResult result,
        bool fromCache)
    {
        // Create a copy with cache metadata in reasoning
        var reasoningSuffix = fromCache ? " [From Cache]" : "";
        return new IntentClassificationResult
        {
            Intent = result.Intent,
            Route = result.Route,
            Confidence = result.Confidence,
            Reasoning = result.Reasoning + reasoningSuffix,
            NormalizedQuery = result.NormalizedQuery,
            Method = result.Method, // Keep original classification method
            DetectedEntities = result.DetectedEntities,
            Warnings = result.Warnings,
            MatchedKeywords = result.MatchedKeywords,
            ForbiddenReason = result.ForbiddenReason,
            SafeAlternatives = result.SafeAlternatives
        };
    }

    private static string ComputeHash(string input)
    {
        // Use SHA256 for consistent hashing
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    #endregion
}

/// <summary>
/// Interface for intent caching service
/// </summary>
public interface IIntentCacheService
{
    Task<IntentClassificationResult?> GetCachedAsync(string question, CancellationToken ct = default);
    Task CacheAsync(string question, IntentClassificationResult result, CancellationToken ct = default);
    Task InvalidateAsync(string question, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}


