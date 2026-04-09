using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// PHASE-2 TASK 2.2d: Cache for SQL generation results (question → SQL).
/// Prevents redundant LLM calls for repeated or similar questions.
/// </summary>
public class QueryResultCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<QueryResultCache> _logger;

    // Cache configuration
    private const int MaxCacheSize = 500;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public QueryResultCache(IMemoryCache cache, ILogger<QueryResultCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get cached SQL generation result.
    /// </summary>
    public SqlGenerationResult? Get(string question, string connectionId, string schemaFingerprint)
    {
        var key = GenerateCacheKey(question, connectionId, schemaFingerprint);

        if (_cache.TryGetValue(key, out SqlGenerationResult? cached))
        {
            _logger.LogInformation(
                "[QueryCache] ✓ HIT for question: '{Question}' → SQL: {Sql}",
                question.Length > 50 ? question.Substring(0, 50) + "..." : question,
                cached.Sql.Length > 50 ? cached.Sql.Substring(0, 50) + "..." : cached.Sql);
            return cached;
        }

        _logger.LogDebug("[QueryCache] MISS for key: {Key}", key.Substring(0, 16));
        return null;
    }

    /// <summary>
    /// Store SQL generation result in cache.
    /// </summary>
    public void Set(
        string question,
        string connectionId,
        string schemaFingerprint,
        SqlGenerationResult result)
    {
        var key = GenerateCacheKey(question, connectionId, schemaFingerprint);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DefaultTtl,
            Size = 1 // For size-based eviction
        };

        _cache.Set(key, result, options);
        _logger.LogDebug(
            "[QueryCache] SET for key: {Key}, SQL length: {Length}",
            key.Substring(0, 16), result.Sql.Length);
    }

    /// <summary>
    /// Get or create SQL generation result.
    /// </summary>
    public async Task<SqlGenerationResult> GetOrCreateAsync(
        string question,
        string connectionId,
        string schemaFingerprint,
        Func<Task<SqlGenerationResult>> factory)
    {
        // Try cache first
        var cached = Get(question, connectionId, schemaFingerprint);
        if (cached != null)
        {
            return cached;
        }

        // Cache miss - call factory (expensive LLM call)
        _logger.LogDebug("[QueryCache] Calling factory for new SQL generation");
        var result = await factory();

        // Store in cache
        Set(question, connectionId, schemaFingerprint, result);

        return result;
    }

    /// <summary>
    /// Invalidate cache for a specific connection (e.g., after schema change).
    /// </summary>
    public void InvalidateConnection(string connectionId)
    {
        // Note: IMemoryCache doesn't support pattern-based removal
        // This is a limitation - consider using Redis for production
        _logger.LogWarning(
            "[QueryCache] InvalidateConnection({ConnectionId}) called but IMemoryCache doesn't support pattern removal",
            connectionId);
    }

    /// <summary>
    /// Generate cache key from question, connection ID, and schema fingerprint.
    /// Schema fingerprint ensures cache invalidation when schema changes.
    /// </summary>
    private static string GenerateCacheKey(
        string question,
        string connectionId,
        string schemaFingerprint)
    {
        // Normalize question (lowercase, trim, remove extra spaces)
        var normalized = NormalizeQuestion(question);

        // Combine with connection ID and schema fingerprint
        var input = $"{normalized}|{connectionId}|{schemaFingerprint}";

        // Generate SHA256 hash
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);

        // Convert to hex string
        return $"query:{Convert.ToHexString(hash)}";
    }

    /// <summary>
    /// Normalize question for consistent cache key generation.
    /// </summary>
    private static string NormalizeQuestion(string question)
    {
        // Lowercase
        var normalized = question.ToLowerInvariant();

        // Trim
        normalized = normalized.Trim();

        // Remove extra whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

        // Remove punctuation at end
        normalized = normalized.TrimEnd('.', '?', '!');

        return normalized;
    }
}
