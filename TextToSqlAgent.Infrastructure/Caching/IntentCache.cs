using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// PHASE-2 TASK 2.2b: Cache for intent classification results.
/// Prevents redundant LLM calls for similar questions.
/// </summary>
public class IntentCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<IntentCache> _logger;

    // Cache configuration
    private const int MaxCacheSize = 1000;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public IntentCache(IMemoryCache cache, ILogger<IntentCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get cached intent classification result.
    /// </summary>
    public IntentClassificationResult? Get(string question, string connectionId)
    {
        var key = GenerateCacheKey(question, connectionId);

        if (_cache.TryGetValue(key, out IntentClassificationResult? cached))
        {
            _logger.LogDebug("[IntentCache] HIT for key: {Key}", key.Substring(0, 16));
            return cached;
        }

        _logger.LogDebug("[IntentCache] MISS for key: {Key}", key.Substring(0, 16));
        return null;
    }

    /// <summary>
    /// Store intent classification result in cache.
    /// </summary>
    public void Set(string question, string connectionId, IntentClassificationResult result)
    {
        var key = GenerateCacheKey(question, connectionId);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DefaultTtl,
            Size = 1 // For size-based eviction
        };

        _cache.Set(key, result, options);
        _logger.LogDebug("[IntentCache] SET for key: {Key}, Intent: {Intent}",
            key.Substring(0, 16), result.Intent);
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
        var cached = Get(question, connectionId);
        if (cached != null)
        {
            return cached;
        }

        // Cache miss - call factory
        _logger.LogDebug("[IntentCache] Calling factory for new classification");
        var result = await factory();

        // Store in cache
        Set(question, connectionId, result);

        return result;
    }

    /// <summary>
    /// Clear all cached intents.
    /// </summary>
    public void Clear()
    {
        // Note: IMemoryCache doesn't have a Clear() method
        // This is a limitation - consider using a custom cache implementation
        _logger.LogWarning("[IntentCache] Clear() called but IMemoryCache doesn't support clearing all entries");
    }

    /// <summary>
    /// Generate cache key from question and connection ID.
    /// Uses SHA256 hash for consistent key generation.
    /// </summary>
    private static string GenerateCacheKey(string question, string connectionId)
    {
        // Normalize question (lowercase, trim)
        var normalized = question.ToLowerInvariant().Trim();

        // Combine with connection ID
        var input = $"{normalized}|{connectionId}";

        // Generate SHA256 hash
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);

        // Convert to hex string
        return $"intent:{Convert.ToHexString(hash)}";
    }
}
