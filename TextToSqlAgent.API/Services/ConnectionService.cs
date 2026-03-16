using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Implementation of connection service for managing database connections with repository pattern
/// </summary>
public class ConnectionService : IConnectionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly IDatabaseAdapter _databaseAdapter;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConnectionService> _logger;

    public ConnectionService(
        IUnitOfWork unitOfWork,
        IConnectionEncryptionService encryptionService,
        IDatabaseAdapter databaseAdapter,
        IServiceProvider serviceProvider,
        ILogger<ConnectionService> logger)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _databaseAdapter = databaseAdapter;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IEnumerable<ConnectionResponse>> GetUserConnectionsAsync(string userId)
    {
        try
        {
            var connections = await _unitOfWork.Connections.GetActiveConnectionsAsync(userId);

            var result = new List<ConnectionResponse>();

            foreach (var connection in connections.OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name))
            {
                // Check schema sync status from Qdrant
                SchemaSyncStatus? schemaSyncStatus = null;
                bool isConnected = false;

                try
                {
                    // Extract database name from connection string
                    var connectionString = _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);
                    var databaseName = ExtractDatabaseNameFromConnectionString(connectionString);

                    // Test database connection
                    try
                    {
                        isConnected = await _databaseAdapter.TestConnectionAsync(connectionString);
                    }
                    catch
                    {
                        isConnected = false;
                    }

                    if (!string.IsNullOrEmpty(databaseName))
                    {
                        // Check if collection exists on Qdrant
                        var qdrantService = _serviceProvider.GetRequiredService<TextToSqlAgent.Infrastructure.VectorDB.QdrantService>();
                        qdrantService.SetCollectionName(databaseName);
                        var collectionExists = await qdrantService.CollectionExistsAsync();

                        int tableCount = 0;
                        if (collectionExists)
                        {
                            var collectionInfo = await qdrantService.GetCollectionInfoAsync();
                            tableCount = (int)(collectionInfo?.PointsCount ?? 0);
                        }

                        schemaSyncStatus = new SchemaSyncStatus
                        {
                            LastSyncedAt = collectionExists ? DateTime.UtcNow : null, // Approximate
                            IsInProgress = false,
                            TableCount = tableCount,
                            LastError = null,
                            IsSynced = collectionExists && tableCount > 0
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get schema sync status for connection {ConnectionId}", connection.Id);
                    schemaSyncStatus = new SchemaSyncStatus
                    {
                        LastSyncedAt = null,
                        IsInProgress = false,
                        TableCount = 0,
                        LastError = "Failed to check schema status",
                        IsSynced = false
                    };
                }

                result.Add(new ConnectionResponse
                {
                    Id = connection.Id,
                    Name = connection.Name,
                    Provider = connection.Provider,
                    Host = connection.Host,
                    Port = connection.Port,
                    Database = connection.Database,
                    Username = connection.Username,
                    Description = connection.Description,
                    IsDefault = connection.IsDefault,
                    LastUsedAt = connection.LastUsedAt,
                    CreatedAt = connection.CreatedAt,
                    IsDeleted = connection.IsDeleted,
                    SchemaSync = schemaSyncStatus,
                    IsConnected = isConnected
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving connections for user {UserId}", userId);
            throw;
        }
    }

    public async Task<ConnectionResponse?> GetConnectionAsync(string id, string userId)
    {
        try
        {
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);

            if (connection == null || connection.IsDeleted)
                return null;

            return new ConnectionResponse
            {
                Id = connection.Id,
                Name = connection.Name,
                Provider = connection.Provider,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = connection.Username,
                Description = connection.Description,
                IsDefault = connection.IsDefault,
                CreatedAt = connection.CreatedAt,
                LastUsedAt = connection.LastUsedAt,
                IsConnected = false, // Could test connection here if needed
                SchemaSync = connection.SchemaSyncedAt != null ? new SchemaSyncStatus
                {
                    LastSyncedAt = connection.SchemaSyncedAt,
                    IsInProgress = false,
                    TableCount = connection.Schemas?.Count ?? 0,
                    IsSynced = connection.SchemaSyncedAt != null
                } : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving connection {ConnectionId} for user {UserId}", id, userId);
            throw;
        }
    }

    public async Task<ConnectionResponse> CreateConnectionAsync(CreateConnectionRequest request, string userId)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();

            // Validate provider
            var supportedProviders = new[] { "sqlserver", "postgresql", "mysql", "sqlite" };
            if (!supportedProviders.Contains(request.Provider.ToLowerInvariant()))
            {
                throw new ArgumentException($"Unsupported database provider: {request.Provider}");
            }

            // If this is set as default, unset other defaults
            if (request.IsDefault)
            {
                await _unitOfWork.Connections.UnsetDefaultConnectionsAsync(userId);
            }

            // Create connection entity
            var connection = new Connection
            {
                UserId = userId,
                Name = request.Name,
                Provider = request.Provider.ToLowerInvariant(),
                Host = request.Host,
                Port = request.Port,
                Database = request.Database,
                Username = request.Username,
                Description = request.Description,
                IsDefault = request.IsDefault
            };

            // Encrypt password
            connection.EncryptedPassword = _encryptionService.EncryptPassword(request.Password, connection.Id);

            // Build and encrypt connection string
            var connectionString = _encryptionService.BuildConnectionString(
                connection.Provider, connection.Host, connection.Port,
                connection.Database, connection.Username, request.Password);
            connection.ConnectionString = _encryptionService.EncryptPassword(connectionString, connection.Id);

            await _unitOfWork.Connections.AddAsync(connection);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            _logger.LogInformation("Connection created successfully: {ConnectionId} for user {UserId}", connection.Id, userId);

            // Test the connection in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await TestConnectionInternalAsync(connection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background connection test failed for {ConnectionId}", connection.Id);
                }
            });

            return new ConnectionResponse
            {
                Id = connection.Id,
                Name = connection.Name,
                Provider = connection.Provider,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = connection.Username,
                Description = connection.Description,
                IsDefault = connection.IsDefault,
                CreatedAt = connection.CreatedAt,
                IsConnected = false // Will be tested in background
            };
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Error creating connection for user {UserId}", userId);
            throw;
        }
    }

    public async Task<ConnectionResponse?> UpdateConnectionAsync(string id, UpdateConnectionRequest request, string userId)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();

            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);

            if (connection == null || connection.IsDeleted)
                return null;

            // If this is set as default, unset other defaults
            if (request.IsDefault && !connection.IsDefault)
            {
                await _unitOfWork.Connections.UnsetDefaultConnectionsAsync(userId);
            }

            // Update properties
            connection.Name = request.Name;
            connection.Host = request.Host;
            connection.Port = request.Port;
            connection.Database = request.Database;
            connection.Username = request.Username;
            connection.Description = request.Description;
            connection.IsDefault = request.IsDefault;

            // Update password if provided
            if (!string.IsNullOrEmpty(request.Password))
            {
                connection.EncryptedPassword = _encryptionService.EncryptPassword(request.Password, connection.Id);

                // Rebuild connection string
                var connectionString = _encryptionService.BuildConnectionString(
                    connection.Provider, connection.Host, connection.Port,
                    connection.Database, connection.Username, request.Password);
                connection.ConnectionString = _encryptionService.EncryptPassword(connectionString, connection.Id);
            }

            await _unitOfWork.Connections.UpdateAsync(connection);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            _logger.LogInformation("Connection updated successfully: {ConnectionId}", connection.Id);

            return new ConnectionResponse
            {
                Id = connection.Id,
                Name = connection.Name,
                Provider = connection.Provider,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = connection.Username,
                Description = connection.Description,
                IsDefault = connection.IsDefault,
                CreatedAt = connection.CreatedAt,
                LastUsedAt = connection.LastUsedAt,
                IsConnected = false // Could test connection here if needed
            };
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Error updating connection {ConnectionId}", id);
            throw;
        }
    }

    public async Task<bool> DeleteConnectionAsync(string id, string userId)
    {
        try
        {
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);

            if (connection == null || connection.IsDeleted)
                return false;

            // Soft delete
            connection.IsDeleted = true;

            // If this was the default connection, set another as default
            if (connection.IsDefault)
            {
                var nextConnection = await _unitOfWork.Connections
                    .FirstOrDefaultAsync(c => c.UserId == userId && !c.IsDeleted && c.Id != id);

                if (nextConnection != null)
                {
                    nextConnection.IsDefault = true;
                    await _unitOfWork.Connections.UpdateAsync(nextConnection);
                }
            }

            await _unitOfWork.Connections.UpdateAsync(connection);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Connection soft deleted: {ConnectionId}", connection.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting connection {ConnectionId}", id);
            throw;
        }
    }

    public async Task<TestConnectionResult> TestConnectionAsync(string id, string userId)
    {
        try
        {
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);

            if (connection == null || connection.IsDeleted)
            {
                return new TestConnectionResult
                {
                    Success = false,
                    ErrorMessage = "Connection not found"
                };
            }

            return await TestConnectionInternalAsync(connection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection {ConnectionId}", id);
            return new TestConnectionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> SyncSchemaAsync(string id, string userId)
    {
        try
        {
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);

            if (connection == null || connection.IsDeleted)
                return false;

            // Update last used and schema sync timestamps
            await _unitOfWork.Connections.UpdateLastUsedAsync(connection.Id);
            await _unitOfWork.Connections.UpdateSchemaSyncedAsync(connection.Id);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Schema sync triggered for connection {ConnectionId}", connection.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering schema sync for connection {ConnectionId}", id);
            return false;
        }
    }
    /// <summary>
    /// Set a connection as the default for a user
    /// </summary>
    public async Task<bool> SetDefaultConnectionAsync(string id, string userId)
    {
        try
        {
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);

            if (connection == null || connection.IsDeleted)
                return false;

            // First, unset all other default connections for this user
            var userConnections = await _unitOfWork.Connections.GetByUserIdAsync(userId);
            foreach (var conn in userConnections.Where(c => c.IsDefault && c.Id != id))
            {
                conn.IsDefault = false;
                await _unitOfWork.Connections.UpdateAsync(conn);
            }

            // Set this connection as default
            connection.IsDefault = true;
            await _unitOfWork.Connections.UpdateAsync(connection);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Connection {ConnectionId} set as default for user {UserId}", connection.Id, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default connection {ConnectionId} for user {UserId}", id, userId);
            return false;
        }
    }

    private async Task<TestConnectionResult> TestConnectionInternalAsync(Connection connection)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Decrypt connection string
            var connectionString = _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);

            // Test connection using database adapter
            var isValid = await _databaseAdapter.TestConnectionAsync(connectionString);
            stopwatch.Stop();

            if (isValid)
            {
                // Update last used timestamp
                await _unitOfWork.Connections.UpdateLastUsedAsync(connection.Id);
                await _unitOfWork.SaveChangesAsync();

                return new TestConnectionResult
                {
                    Success = true,
                    ResponseTime = stopwatch.Elapsed,
                    DatabaseVersion = "Connected successfully"
                };
            }
            else
            {
                return new TestConnectionResult
                {
                    Success = false,
                    ErrorMessage = "Connection validation failed",
                    ResponseTime = stopwatch.Elapsed
                };
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TestConnectionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Extract database name from connection string
    /// </summary>
    private string ExtractDatabaseNameFromConnectionString(string connectionString)
    {
        try
        {
            // Parse SQL Server connection string
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return builder.InitialCatalog;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse connection string to extract database name");
            return string.Empty;
        }
    }
}