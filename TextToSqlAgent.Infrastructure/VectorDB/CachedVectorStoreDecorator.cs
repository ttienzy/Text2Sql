using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.VectorDB;

/// <summary>
/// Decorator that adds LRU caching to any IVectorStore implementation.
/// Greatly reduces latency by preventing redundant API calls to Qdrant for identical queries.
/// </summary>
public class CachedVectorStoreDecorator : IVectorStore
{
    private readonly IVectorStore _inner;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedVectorStoreDecorator> _logger;
    private readonly TimeSpan _cacheDuration;

    public CachedVectorStoreDecorator(
        IVectorStore inner,
        IMemoryCache cache,
        ILogger<CachedVectorStoreDecorator> logger,
        TimeSpan? cacheDuration = null)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5); // Default 5 mins TTL
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return _inner.IsAvailableAsync(cancellationToken);
    }

    public Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        return _inner.EnsureCollectionAsync(cancellationToken);
    }

    // Unfiltered search maps to filtered search internally
    public Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(queryVector, limit, scoreThreshold, null, cancellationToken);
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        Dictionary<string, object>? filter,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = ComputeCacheKey(queryVector, limit, scoreThreshold, filter);

        if (_cache.TryGetValue(cacheKey, out List<VectorSearchResult>? cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("[CachedVectorStore] ⚡ Cache HIT for query (limit: {Limit}, threshold: {Threshold})", limit, scoreThreshold);
            return cachedResult;
        }

        _logger.LogDebug("[CachedVectorStore] 📉 Cache MISS for query. Fetching from inner store.");
        var result = await _inner.SearchAsync(queryVector, limit, scoreThreshold, filter, cancellationToken);

        // Cache the successful results with size limits (approximate size ~ 1 unit per item)
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheDuration,
            Size = result.Count > 0 ? result.Count : 1
        };

        _cache.Set(cacheKey, result, cacheOptions);

        return result;
    }

    public async Task UpsertPointsAsync(List<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        await _inner.UpsertPointsAsync(points, cancellationToken);
        
        // Cache invalidation isn't strictly necessary here if we assume points are static enough for a 5min TTL,
        // but if strict consistency is needed, we would need to invalidate all queries related to this collection.
        // For schema caching, 5 mins TTL without strict invalidation on upsert is usually an acceptable trade-off.
    }

    public Task<long> GetPointCountAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetPointCountAsync(cancellationToken);
    }

    public Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        return _inner.DeleteCollectionAsync(cancellationToken);
    }

    public Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        return _inner.CollectionExistsAsync(cancellationToken);
    }

    public Task StoreSchemaFingerprintAsync(SchemaFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        return _inner.StoreSchemaFingerprintAsync(fingerprint, cancellationToken);
    }

    public Task<SchemaFingerprint?> GetStoredFingerprintAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetStoredFingerprintAsync(cancellationToken);
    }

    private string ComputeCacheKey(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        Dictionary<string, object>? filter)
    {
        using var sha256 = SHA256.Create();
        
        // Hash the vector array bytes
        var vectorBytes = new byte[queryVector.Length * sizeof(float)];
        Buffer.BlockCopy(queryVector, 0, vectorBytes, 0, vectorBytes.Length);
        
        var vectorHash = Convert.ToHexString(sha256.ComputeHash(vectorBytes));

        var filterHash = string.Empty;
        if (filter != null)
        {
            // Create a deterministic hash of the filter by sorting keys
            var filterString = string.Join(";", filter.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
            filterHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(filterString)));
        }

        return $"vsearch:{vectorHash}:{limit}:{scoreThreshold}:{filterHash}";
    }
}
