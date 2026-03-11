using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// Distributed cache service for caching schemas, embeddings, and results
/// </summary>
public class CacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CacheService> _logger;
    private readonly CacheOptions _options;

    public CacheService(
        IDistributedCache cache,
        ILogger<CacheService> logger,
        CacheOptions? options = null)
    {
        _cache = cache;
        _logger = logger;
        _options = options ?? new CacheOptions();
    }

    /// <summary>
    /// Get cached value
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var cachedData = await _cache.GetStringAsync(key, ct);

            if (string.IsNullOrEmpty(cachedData))
            {
                _logger.LogDebug("Cache miss: {Key}", key);
                return default;
            }

            _logger.LogDebug("Cache hit: {Key}", key);
            return JsonSerializer.Deserialize<T>(cachedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache get failed for key: {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// Set cached value with expiration
    /// </summary>
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? _options.DefaultExpiration
            };

            await _cache.SetStringAsync(key, serialized, options, ct);
            _logger.LogDebug("Cache set: {Key} (expires in {Expiration})", key, options.AbsoluteExpirationRelativeToNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache set failed for key: {Key}", key);
        }
    }

    /// <summary>
    /// Get or create cached value
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        // Try to get from cache
        var cached = await GetAsync<T>(key, ct);
        if (cached != null)
        {
            return cached;
        }

        // Create new value
        _logger.LogDebug("Cache miss, creating value for: {Key}", key);
        var value = await factory();

        // Cache it
        await SetAsync(key, value, expiration, ct);

        return value;
    }

    /// <summary>
    /// Remove cached value
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(key, ct);
            _logger.LogDebug("Cache removed: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache remove failed for key: {Key}", key);
        }
    }

    /// <summary>
    /// Remove multiple keys by pattern (requires Redis)
    /// </summary>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken ct = default)
    {
        _logger.LogWarning("RemoveByPattern not implemented for generic IDistributedCache");
        // This would require Redis-specific implementation
        await Task.CompletedTask;
    }

    /// <summary>
    /// Generate cache key for schema
    /// </summary>
    public static string GetSchemaKey(string databaseId) => $"schema:{databaseId}";

    /// <summary>
    /// Generate cache key for embeddings
    /// </summary>
    public static string GetEmbeddingKey(string text) => $"embedding:{GetHash(text)}";

    /// <summary>
    /// Generate cache key for SQL result
    /// </summary>
    public static string GetSqlResultKey(string sql, string databaseId) =>
        $"sql_result:{databaseId}:{GetHash(sql)}";

    /// <summary>
    /// Generate cache key for schema linking
    /// </summary>
    public static string GetSchemaLinkingKey(string question, string databaseId) =>
        $"schema_linking:{databaseId}:{GetHash(question)}";

    /// <summary>
    /// Get hash of string for cache key
    /// </summary>
    private static string GetHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16]; // First 16 chars
    }
}

/// <summary>
/// Cache configuration options
/// </summary>
public class CacheOptions
{
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan SchemaExpiration { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan EmbeddingExpiration { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan SqlResultExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SchemaLinkingExpiration { get; set; } = TimeSpan.FromHours(1);
    public bool EnableCaching { get; set; } = true;
}
