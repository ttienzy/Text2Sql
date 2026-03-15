using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for caching database schema to reduce LLM calls
/// </summary>
public interface ISchemaCache
{
    /// <summary>
    /// Get cached schema by connection ID
    /// </summary>
    Task<DatabaseSchema?> GetAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set cached schema for connection ID
    /// </summary>
    Task SetAsync(string connectionId, DatabaseSchema schema, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached schema or create if not exists
    /// </summary>
    Task<DatabaseSchema> GetOrSetAsync(
        string connectionId,
        Func<Task<DatabaseSchema>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached schema for connection ID
    /// </summary>
    Task RemoveAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all cached schemas
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if cache is available (Redis connected or memory fallback)
    /// </summary>
    bool IsAvailable { get; }
}