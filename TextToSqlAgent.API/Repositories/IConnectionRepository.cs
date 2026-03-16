using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository interface for Connection entity operations
/// </summary>
public interface IConnectionRepository : IRepository<Connection>
{
    /// <summary>
    /// Get all connections for a specific user
    /// </summary>
    Task<IEnumerable<Connection>> GetByUserIdAsync(string userId);

    /// <summary>
    /// Get user's default connection
    /// </summary>
    Task<Connection?> GetDefaultConnectionAsync(string userId);

    /// <summary>
    /// Get connection by ID for specific user (authorization check)
    /// </summary>
    Task<Connection?> GetByIdAndUserIdAsync(string id, string userId);

    /// <summary>
    /// Get active (non-deleted) connections for user
    /// </summary>
    Task<IEnumerable<Connection>> GetActiveConnectionsAsync(string userId);

    /// <summary>
    /// Unset all default connections for a user
    /// </summary>
    Task UnsetDefaultConnectionsAsync(string userId);

    /// <summary>
    /// Update last used timestamp
    /// </summary>
    Task UpdateLastUsedAsync(string connectionId);

    /// <summary>
    /// Update schema sync timestamp
    /// </summary>
    Task UpdateSchemaSyncedAsync(string connectionId);
}