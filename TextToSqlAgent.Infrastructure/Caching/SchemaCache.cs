using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// Schema cache implementation with Redis (primary) and in-memory fallback
/// Cache key format: schema:{connectionId}
/// TTL: 10 hours (36000 seconds)
/// </summary>
public class SchemaCache : ISchemaCache
{
    private readonly IDistributedCache? _distributedCache;
    private readonly MemorySchemaCache _memoryCache;
    private readonly ILogger<SchemaCache> _logger;
    private readonly SchemaCacheOptions _options;

    // Default TTL: 10 hours
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(10);

    public SchemaCache(
        IDistributedCache? distributedCache,
        ILogger<SchemaCache> logger,
        SchemaCacheOptions? options = null)
    {
        _distributedCache = distributedCache;
        _memoryCache = new MemorySchemaCache();
        _logger = logger;
        _options = options ?? new SchemaCacheOptions();
    }

    /// <summary>
    /// Check if cache is available (Redis connected or memory fallback)
    /// </summary>
    public bool IsAvailable => _distributedCache != null || true; // Always available with memory fallback

    /// <summary>
    /// Get cached schema by connection ID
    /// </summary>
    public async Task<DatabaseSchema?> GetAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var key = GetCacheKey(connectionId);

        // Try Redis first if available
        if (_distributedCache != null)
        {
            try
            {
                var cached = await _distributedCache.GetStringAsync(key, cancellationToken);
                if (!string.IsNullOrEmpty(cached))
                {
                    _logger.LogDebug("[SchemaCache] Redis HIT for key: {Key}", key);
                    return JsonSerializer.Deserialize<DatabaseSchema>(cached);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaCache] Redis get failed, falling back to memory");
            }
        }

        // Fallback to memory cache
        var memoryResult = _memoryCache.Get(connectionId);
        if (memoryResult != null)
        {
            _logger.LogDebug("[SchemaCache] Memory HIT for key: {Key}", key);
            return memoryResult;
        }

        _logger.LogDebug("[SchemaCache] MISS for key: {Key}", key);
        return null;
    }

    /// <summary>
    /// Set cached schema for connection ID
    /// </summary>
    public async Task SetAsync(string connectionId, DatabaseSchema schema, CancellationToken cancellationToken = default)
    {
        var key = GetCacheKey(connectionId);
        var serialized = JsonSerializer.Serialize(schema);

        // Try Redis first if available
        if (_distributedCache != null)
        {
            try
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.Ttl ?? DefaultTtl
                };
                await _distributedCache.SetStringAsync(key, serialized, options, cancellationToken);
                _logger.LogDebug("[SchemaCache] Redis SET for key: {Key}, TTL: {Ttl}", key, _options.Ttl ?? DefaultTtl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaCache] Redis set failed, using memory only");
            }
        }

        // Also store in memory cache as backup
        _memoryCache.Set(connectionId, schema, _options.Ttl ?? DefaultTtl);
        _logger.LogDebug("[SchemaCache] Memory SET for key: {Key}", key);
    }

    /// <summary>
    /// Get cached schema or create if not exists
    /// </summary>
    public async Task<DatabaseSchema> GetOrSetAsync(
        string connectionId,
        Func<Task<DatabaseSchema>> factory,
        CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        var cached = await GetAsync(connectionId, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // Cache miss - create new value
        _logger.LogInformation("[SchemaCache] Cache miss for connection: {ConnectionId}, fetching from database", connectionId);
        var schema = await factory();

        // Store in cache
        await SetAsync(connectionId, schema, cancellationToken);

        return schema;
    }

    /// <summary>
    /// Remove cached schema for connection ID
    /// </summary>
    public async Task RemoveAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var key = GetCacheKey(connectionId);

        // Try Redis first if available
        if (_distributedCache != null)
        {
            try
            {
                await _distributedCache.RemoveAsync(key, cancellationToken);
                _logger.LogDebug("[SchemaCache] Redis REMOVE for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaCache] Redis remove failed");
            }
        }

        // Also remove from memory cache
        _memoryCache.Remove(connectionId);
        _logger.LogDebug("[SchemaCache] Memory REMOVE for key: {Key}", key);
    }

    /// <summary>
    /// Clear all cached schemas (memory only - Redis would require pattern scan)
    /// </summary>
    public Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        _memoryCache.ClearAll();
        _logger.LogInformation("[SchemaCache] All schema cache cleared (memory only)");
        return Task.CompletedTask;
    }

    private static string GetCacheKey(string connectionId) => $"schema:{connectionId}";
}

/// <summary>
/// In-memory schema cache fallback (thread-safe)
/// </summary>
internal class MemorySchemaCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly Timer? _cleanupTimer;

    public MemorySchemaCache()
    {
        // Cleanup expired entries every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public DatabaseSchema? Get(string connectionId)
    {
        var key = GetCacheKey(connectionId);
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired)
            {
                _cache.TryRemove(key, out _);
                return null;
            }
            return entry.Value;
        }
        return null;
    }

    public void Set(string connectionId, DatabaseSchema value, TimeSpan ttl)
    {
        var key = GetCacheKey(connectionId);
        _cache[key] = new CacheEntry
        {
            Value = value,
            ExpiresAt = DateTime.UtcNow + ttl
        };
    }

    public void Remove(string connectionId)
    {
        var key = GetCacheKey(connectionId);
        _cache.TryRemove(key, out _);
    }

    public void ClearAll()
    {
        _cache.Clear();
    }

    private static string GetCacheKey(string connectionId) => $"schema:{connectionId}";

    private void CleanupExpiredEntries(object? state)
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private class CacheEntry
    {
        public DatabaseSchema Value { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}

/// <summary>
/// Schema cache configuration options
/// </summary>
public class SchemaCacheOptions
{
    /// <summary>
    /// Time to live for cached schemas (default: 10 hours)
    /// </summary>
    public TimeSpan? Ttl { get; set; } = TimeSpan.FromHours(10);

    /// <summary>
    /// Enable or disable caching
    /// </summary>
    public bool EnableCaching { get; set; } = true;
}