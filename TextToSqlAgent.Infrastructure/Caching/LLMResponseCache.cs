using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// PHASE-2 TASK 2.2e: Cache for LLM responses based on prompt.
/// Prevents redundant LLM calls for identical or similar prompts.
/// </summary>
public class LLMResponseCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LLMResponseCache> _logger;

    // Cache configuration
    private const int MaxCacheSize = 500;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    public LLMResponseCache(IMemoryCache cache, ILogger<LLMResponseCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get cached LLM response.
    /// </summary>
    public string? Get(string systemPrompt, string userPrompt, string model = "default")
    {
        var key = GenerateCacheKey(systemPrompt, userPrompt, model);

        if (_cache.TryGetValue(key, out string? cached))
        {
            _logger.LogDebug("[LLMCache] HIT for key: {Key}", key.Substring(0, 16));
            return cached;
        }

        _logger.LogDebug("[LLMCache] MISS for key: {Key}", key.Substring(0, 16));
        return null;
    }

    /// <summary>
    /// Store LLM response in cache.
    /// </summary>
    public void Set(string systemPrompt, string userPrompt, string response, string model = "default")
    {
        var key = GenerateCacheKey(systemPrompt, userPrompt, model);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DefaultTtl,
            Size = 1 // For size-based eviction
        };

        _cache.Set(key, response, options);
        _logger.LogDebug("[LLMCache] SET for key: {Key}, Response length: {Length}",
            key.Substring(0, 16), response.Length);
    }

    /// <summary>
    /// Get or create LLM response.
    /// </summary>
    public async Task<string> GetOrCreateAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        Func<Task<string>> factory)
    {
        // Try cache first
        var cached = Get(systemPrompt, userPrompt, model);
        if (cached != null)
        {
            return cached;
        }

        // Cache miss - call factory
        _logger.LogDebug("[LLMCache] Calling LLM for new response");
        var response = await factory();

        // Store in cache
        Set(systemPrompt, userPrompt, response, model);

        return response;
    }

    /// <summary>
    /// Clear all cached LLM responses.
    /// </summary>
    public void Clear()
    {
        // Note: IMemoryCache doesn't have a Clear() method
        // This is a limitation - consider using a custom cache implementation
        _logger.LogWarning("[LLMCache] Clear() called but IMemoryCache doesn't support clearing all entries");
    }

    /// <summary>
    /// Generate cache key from prompts and model.
    /// Uses SHA256 hash for consistent key generation.
    /// </summary>
    private static string GenerateCacheKey(string systemPrompt, string userPrompt, string model)
    {
        // Normalize prompts (trim whitespace)
        var normalizedSystem = systemPrompt.Trim();
        var normalizedUser = userPrompt.Trim();

        // Combine with model
        var input = $"{normalizedSystem}|{normalizedUser}|{model}";

        // Generate SHA256 hash
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);

        // Convert to hex string
        return $"llm:{Convert.ToHexString(hash)}";
    }
}
