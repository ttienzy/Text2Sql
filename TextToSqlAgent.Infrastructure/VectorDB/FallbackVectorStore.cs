using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.VectorDB;

/// <summary>
/// Fallback vector store that tries primary store first, then falls back to secondary
/// Provides high availability when primary vector store (Qdrant) is unavailable
/// </summary>
public class FallbackVectorStore : IVectorStore
{
    private readonly IVectorStore _primary;
    private readonly IVectorStore _fallback;
    private readonly ILogger<FallbackVectorStore> _logger;

    public FallbackVectorStore(
        IVectorStore primary,
        IVectorStore fallback,
        ILogger<FallbackVectorStore> logger)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Check primary first
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            return true;
        }

        _logger.LogWarning("[FallbackVectorStore] Primary store unavailable, checking fallback");

        // Check fallback
        return await _fallback.IsAvailableAsync(cancellationToken);
    }

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.EnsureCollectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FallbackVectorStore] Primary collection setup failed, using fallback");
            await _fallback.EnsureCollectionAsync(cancellationToken);
        }
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        CancellationToken cancellationToken = default)
    {
        return await SearchAsync(queryVector, limit, scoreThreshold, null, cancellationToken);
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        Dictionary<string, object>? filter,
        CancellationToken cancellationToken = default)
    {
        // Try primary first
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            try
            {
                var results = await _primary.SearchAsync(queryVector, limit, scoreThreshold, filter, cancellationToken);
                _logger.LogDebug("[FallbackVectorStore] Primary search returned {Count} results", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Primary search failed, using fallback");
            }
        }

        // Fallback
        _logger.LogInformation("[FallbackVectorStore] Using fallback store for search");
        return await _fallback.SearchAsync(queryVector, limit, scoreThreshold, filter, cancellationToken);
    }

    public async Task UpsertPointsAsync(
        List<VectorPoint> points,
        CancellationToken cancellationToken = default)
    {
        var primarySuccess = false;

        // Try primary
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            try
            {
                await _primary.UpsertPointsAsync(points, cancellationToken);
                primarySuccess = true;
                _logger.LogDebug("[FallbackVectorStore] Primary upsert successful");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Primary upsert failed");
            }
        }

        // Always sync to fallback for redundancy
        try
        {
            await _fallback.UpsertPointsAsync(points, cancellationToken);
            _logger.LogDebug("[FallbackVectorStore] Fallback upsert successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FallbackVectorStore] Fallback upsert failed");

            // If both failed, throw
            if (!primarySuccess)
            {
                throw new InvalidOperationException("Both primary and fallback upsert failed", ex);
            }
        }
    }

    public async Task<long> GetPointCountAsync(CancellationToken cancellationToken = default)
    {
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            try
            {
                return await _primary.GetPointCountAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Primary count failed, using fallback");
            }
        }

        return await _fallback.GetPointCountAsync(cancellationToken);
    }

    public async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        // Delete from both stores
        var tasks = new List<Task>();

        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _primary.DeleteCollectionAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[FallbackVectorStore] Primary delete failed");
                }
            }, cancellationToken));
        }

        tasks.Add(Task.Run(async () =>
        {
            try
            {
                await _fallback.DeleteCollectionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Fallback delete failed");
            }
        }, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            try
            {
                return await _primary.CollectionExistsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Primary exists check failed, using fallback");
            }
        }

        return await _fallback.CollectionExistsAsync(cancellationToken);
    }

    public async Task StoreSchemaFingerprintAsync(
        SchemaFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var primarySuccess = false;

        // Try primary
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            try
            {
                await _primary.StoreSchemaFingerprintAsync(fingerprint, cancellationToken);
                primarySuccess = true;
                _logger.LogDebug("[FallbackVectorStore] Primary fingerprint store successful");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Primary fingerprint store failed");
            }
        }

        // Always sync to fallback for redundancy
        try
        {
            await _fallback.StoreSchemaFingerprintAsync(fingerprint, cancellationToken);
            _logger.LogDebug("[FallbackVectorStore] Fallback fingerprint store successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FallbackVectorStore] Fallback fingerprint store failed");

            // If both failed, throw
            if (!primarySuccess)
            {
                throw new InvalidOperationException("Both primary and fallback fingerprint store failed", ex);
            }
        }
    }

    public async Task<SchemaFingerprint?> GetStoredFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        if (await _primary.IsAvailableAsync(cancellationToken))
        {
            try
            {
                return await _primary.GetStoredFingerprintAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FallbackVectorStore] Primary fingerprint retrieval failed, using fallback");
            }
        }

        return await _fallback.GetStoredFingerprintAsync(cancellationToken);
    }
}
