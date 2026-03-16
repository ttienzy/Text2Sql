using TextToSqlAgent.API.DTOs;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for managing database connections
/// </summary>
public interface IConnectionService
{
    /// <summary>
    /// Get all connections for a user
    /// </summary>
    Task<IEnumerable<ConnectionResponse>> GetUserConnectionsAsync(string userId);

    /// <summary>
    /// Get a specific connection by ID
    /// </summary>
    Task<ConnectionResponse?> GetConnectionAsync(string id, string userId);

    /// <summary>
    /// Create a new connection
    /// </summary>
    Task<ConnectionResponse> CreateConnectionAsync(CreateConnectionRequest request, string userId);

    /// <summary>
    /// Update an existing connection
    /// </summary>
    Task<ConnectionResponse?> UpdateConnectionAsync(string id, UpdateConnectionRequest request, string userId);

    /// <summary>
    /// Delete a connection (soft delete)
    /// </summary>
    Task<bool> DeleteConnectionAsync(string id, string userId);

    /// <summary>
    /// Test connection connectivity
    /// </summary>
    Task<TestConnectionResult> TestConnectionAsync(string id, string userId);

    /// <summary>
    /// Trigger schema synchronization for a connection
    /// </summary>
    Task<bool> SyncSchemaAsync(string id, string userId);

    /// <summary>
    /// Set a connection as the default for a user
    /// </summary>
    Task<bool> SetDefaultConnectionAsync(string id, string userId);
}