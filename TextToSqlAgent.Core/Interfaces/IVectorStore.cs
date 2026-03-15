using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Abstraction for vector database operations
/// Allows switching between Qdrant, Pinecone, Milvus, or in-memory implementations
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Check if vector store is available and healthy
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure collection/index exists with correct configuration
    /// </summary>
    Task EnsureCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for similar vectors
    /// </summary>
    Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int limit,
        float scoreThreshold,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or update vector points
    /// </summary>
    Task UpsertPointsAsync(
        List<VectorPoint> points,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total number of points in collection
    /// </summary>
    Task<long> GetPointCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete collection/index
    /// </summary>
    Task DeleteCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if collection exists
    /// </summary>
    Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Store schema fingerprint metadata in the collection
    /// </summary>
    Task StoreSchemaFingerprintAsync(
        SchemaFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve stored schema fingerprint from the collection
    /// Returns null if no fingerprint is stored
    /// </summary>
    Task<SchemaFingerprint?> GetStoredFingerprintAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from vector search
/// </summary>
public class VectorSearchResult
{
    public string Id { get; set; } = string.Empty;
    public float Score { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();
}

/// <summary>
/// Vector point for indexing
/// </summary>
public class VectorPoint
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Payload { get; set; } = new();
}
