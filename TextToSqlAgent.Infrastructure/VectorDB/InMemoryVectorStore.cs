using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Infrastructure.VectorDB;

/// <summary>
/// In-memory vector store implementation using cosine similarity
/// Used as fallback when Qdrant is unavailable
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly List<VectorPoint> _points = new();
    private readonly ILogger<InMemoryVectorStore> _logger;
    private readonly object _lock = new();
    private bool _collectionExists = false;

    public InMemoryVectorStore(ILogger<InMemoryVectorStore> logger)
    {
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // In-memory store is always available
        return Task.FromResult(true);
    }

    public Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_collectionExists)
            {
                _logger.LogInformation("[InMemoryVectorStore] Collection created");
                _collectionExists = true;
            }
        }
        return Task.CompletedTask;
    }

    public Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_points.Count == 0)
            {
                _logger.LogWarning("[InMemoryVectorStore] No points indexed");
                return Task.FromResult(new List<VectorSearchResult>());
            }

            var results = _points
                .Select(p => new
                {
                    Point = p,
                    Score = CosineSimilarity(queryVector, p.Vector)
                })
                .Where(x => x.Score >= scoreThreshold)
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => new VectorSearchResult
                {
                    Id = x.Point.Id,
                    Score = x.Score,
                    Payload = new Dictionary<string, object>(x.Point.Payload)
                })
                .ToList();

            _logger.LogDebug(
                "[InMemoryVectorStore] Search found {Count} results (threshold: {Threshold})",
                results.Count, scoreThreshold);

            return Task.FromResult(results);
        }
    }

    public Task UpsertPointsAsync(
        List<VectorPoint> points,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var point in points)
            {
                // Remove existing point with same ID
                _points.RemoveAll(p => p.Id == point.Id);

                // Add new point
                _points.Add(new VectorPoint
                {
                    Id = point.Id,
                    Vector = point.Vector.ToArray(), // Clone array
                    Payload = new Dictionary<string, object>(point.Payload)
                });
            }

            _logger.LogInformation(
                "[InMemoryVectorStore] Upserted {Count} points, total: {Total}",
                points.Count, _points.Count);
        }

        return Task.CompletedTask;
    }

    public Task<long> GetPointCountAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult((long)_points.Count);
        }
    }

    public Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _points.Clear();
            _collectionExists = false;
            _logger.LogInformation("[InMemoryVectorStore] Collection deleted");
        }
        return Task.CompletedTask;
    }

    public Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_collectionExists);
        }
    }

    /// <summary>
    /// Calculate cosine similarity between two vectors
    /// Returns value between -1 and 1 (1 = identical, 0 = orthogonal, -1 = opposite)
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have same length");
        }

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = MathF.Sqrt(magnitudeA);
        magnitudeB = MathF.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0;
        }

        return dotProduct / (magnitudeA * magnitudeB);
    }
}
