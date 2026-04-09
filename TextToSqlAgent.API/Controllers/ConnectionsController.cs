using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for managing database connections
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class ConnectionsController : BaseController
{
    private readonly IConnectionService _connectionService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly EnhancedAgentOrchestrator? _orchestrator;

    public ConnectionsController(
        IConnectionService connectionService,
        IUnitOfWork unitOfWork,
        IConnectionEncryptionService encryptionService,
        ILogger<ConnectionsController> logger,
        EnhancedAgentOrchestrator? orchestrator = null) : base(logger)
    {
        _connectionService = connectionService;
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Get all connections for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConnectionResponse>>> GetConnections()
    {
        try
        {
            var userId = GetRequiredUserId();
            var connections = await _connectionService.GetUserConnectionsAsync(userId);
            return Ok(connections);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "retrieving connections");
        }
    }

    /// <summary>
    /// Get a specific connection by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConnectionResponse>> GetConnection(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();
            var connection = await _connectionService.GetConnectionAsync(id, userId);

            if (connection == null)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            return Ok(connection);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"retrieving connection {id}");
        }
    }

    /// <summary>
    /// Create a new database connection
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ConnectionResponse>> CreateConnection([FromBody] CreateConnectionRequest request)
    {
        try
        {
            if (!IsModelValid())
            {
                return GetValidationErrors();
            }

            var userId = GetRequiredUserId();
            var connection = await _connectionService.CreateConnectionAsync(request, userId);

            return CreatedAtAction(nameof(GetConnection), new { id = connection.Id }, connection);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "creating connection");
        }
    }

    /// <summary>
    /// Update an existing connection
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ConnectionResponse>> UpdateConnection(string id, [FromBody] UpdateConnectionRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            if (!IsModelValid())
            {
                return GetValidationErrors();
            }

            var userId = GetRequiredUserId();
            var connection = await _connectionService.UpdateConnectionAsync(id, request, userId);

            if (connection == null)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            return Ok(connection);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"updating connection {id}");
        }
    }

    /// <summary>
    /// Delete a connection
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConnection(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();
            var success = await _connectionService.DeleteConnectionAsync(id, userId);

            if (!success)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"deleting connection {id}");
        }
    }

    /// <summary>
    /// Test a database connection
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();
            var result = await _connectionService.TestConnectionAsync(id, userId);

            // ✅ P0 FIX: Auto-scan and cache schema after successful connection test
            if (result.Success)
            {
                try
                {
                    _logger.LogInformation("[TestConnection] Connection successful, scanning schema for {ConnectionId}", id);

                    // Get connection to extract connection string
                    var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);
                    if (connection != null)
                    {
                        var connectionString = _encryptionService.GetConnectionString(connection);

                        // ✅ CRIT-2 FIX: Use DatabaseConfigContext.SetConnectionString() instead of mutating Singleton
                        using (DatabaseConfigContext.SetConnectionString(connectionString))
                        {
                            // Scan database schema
                            var schemaScanner = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.Database.SchemaScanner>();
                            var schema = await schemaScanner.ScanAsync();

                            if (schema != null && schema.Tables.Count > 0)
                            {
                                // Save to cache
                                var schemaCache = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Core.Interfaces.ISchemaCache>();
                                await schemaCache.SetAsync(id, schema);

                                _logger.LogInformation("[TestConnection] Scanned and cached {TableCount} tables for {ConnectionId}",
                                    schema.Tables.Count, id);
                            }
                            else
                            {
                                _logger.LogWarning("[TestConnection] Schema scan returned no tables for {ConnectionId}", id);
                            }
                        } // ← DatabaseConfigContext auto-restores here via IDisposable
                    }
                }
                catch (Exception ex)
                {
                    // Don't fail the test if schema scan fails - just log it
                    _logger.LogError(ex, "[TestConnection] Failed to scan schema for {ConnectionId}, but connection test succeeded", id);
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"testing connection {id}");
        }
    }

    /// <summary>
    /// ✅ P1: Get schema status for a connection
    /// Returns whether schema is loaded in cache and table count
    /// </summary>
    [HttpGet("{id}/schema/status")]
    public async Task<ActionResult<object>> GetSchemaStatus(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();

            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);
            if (connection == null)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            // Check if schema is in cache
            var schemaCache = HttpContext.RequestServices.GetRequiredService<ISchemaCache>();
            var schema = await schemaCache.GetAsync(id);

            return Ok(new
            {
                ConnectionId = id,
                SchemaLoaded = schema != null,
                TableCount = schema?.Tables.Count ?? 0,
                LastLoaded = schema != null ? DateTime.UtcNow : (DateTime?)null
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"getting schema status for connection {id}");
        }
    }

    /// <summary>
    /// Enhanced connection test with schema indexing check and auto indexing
    /// </summary>
    [HttpPost("{id}/test-enhanced")]
    public async Task<ActionResult<EnhancedTestConnectionResult>> TestConnectionEnhanced(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();

            // Get the connection to extract database name
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);
            if (connection == null)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            // Step 1: Test basic database connection
            var basicResult = await _connectionService.TestConnectionAsync(id, userId);

            var result = new EnhancedTestConnectionResult
            {
                Success = basicResult.Success,
                DatabaseConnection = new DatabaseConnectionResult
                {
                    Success = basicResult.Success,
                    ResponseTime = basicResult.ResponseTime.TotalMilliseconds.ToString("F0") + "ms",
                    DatabaseVersion = basicResult.DatabaseVersion ?? "Unknown",
                    ErrorMessage = basicResult.ErrorMessage
                },
                ReadyForChat = false
            };

            if (!basicResult.Success)
            {
                return Ok(result);
            }

            // Step 2: Check schema indexing status using database name
            try
            {
                var vectorSearchService = HttpContext.RequestServices.GetRequiredService<IVectorSearchService>();

                // Extract database name from connection string
                var connectionString = _encryptionService.GetConnectionString(connection);
                var databaseName = ExtractDatabaseNameFromConnectionString(connectionString);

                if (string.IsNullOrEmpty(databaseName))
                {
                    result.SchemaIndexing = new SchemaIndexingResult
                    {
                        CollectionExists = false,
                        SchemasIndexed = 0,
                        ErrorMessage = "Could not extract database name from connection string"
                    };
                    return Ok(result);
                }

                // Generate collection name using the helper
                var collectionName = TextToSqlAgent.Infrastructure.VectorDB.CollectionNameHelper.NormalizeCollectionName(databaseName);
                _logger.LogInformation("Checking schema indexing for database '{DatabaseName}' with collection '{CollectionName}'", databaseName, collectionName);

                // Check if collection exists on Qdrant
                var qdrantService = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.VectorDB.QdrantService>();
                qdrantService.SetCollectionName(databaseName);
                var collectionExists = await qdrantService.CollectionExistsAsync();

                result.SchemaIndexing = new SchemaIndexingResult
                {
                    CollectionExists = collectionExists,
                    SchemasIndexed = 0,
                    AutoIndexed = false
                };

                if (collectionExists)
                {
                    // Get collection info to check schema count
                    var collectionInfo = await qdrantService.GetCollectionInfoAsync();
                    result.SchemaIndexing.SchemasIndexed = (int)(collectionInfo?.PointsCount ?? 0);
                }

                // If collection doesn't exist or has no schemas, trigger auto indexing
                if (!collectionExists || result.SchemaIndexing.SchemasIndexed == 0)
                {
                    _logger.LogInformation("Collection '{CollectionName}' doesn't exist or has no schemas, triggering auto indexing", collectionName);

                    var indexingStartTime = DateTime.UtcNow;

                    // Set the database configuration temporarily for indexing
                    var dbConfig = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.Configuration.DatabaseConfig>();
                    var originalConnectionString = dbConfig.ConnectionString;

                    try
                    {
                        // ✅ CRIT-2 FIX: Use DatabaseConfigContext.SetConnectionString() instead of mutating Singleton
                        using (DatabaseConfigContext.SetConnectionString(connectionString))
                        {
                            // Scan database schema
                            var schemaScanner = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.Database.SchemaScanner>();
                            var schema = await schemaScanner.ScanAsync();

                            // ✅ P0 FIX: Save schema to ISchemaCache for agent processing
                            if (schema != null && schema.Tables.Count > 0)
                            {
                                var schemaCache = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Core.Interfaces.ISchemaCache>();
                                await schemaCache.SetAsync(id, schema);
                                _logger.LogInformation("[TestEnhanced] Saved schema to cache: {TableCount} tables for {ConnectionId}",
                                    schema.Tables.Count, id);
                            }

                            // Create schema fingerprint
                            var hash = System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes(
                                    string.Join(",", schema.Tables.Select(t => $"{t.TableName}:{string.Join(",", t.Columns.Select(c => c.ColumnName))}"))));
                            var hashString = Convert.ToHexString(hash);

                            var fingerprint = new TextToSqlAgent.Core.Models.SchemaFingerprint
                            {
                                Hash = hashString,
                                ComputedAt = DateTime.UtcNow,
                                TableCount = schema.Tables.Count,
                                ColumnCount = schema.Tables.Sum(t => t.Columns.Count),
                                RelationshipCount = schema.Relationships.Count,
                                TableNames = schema.Tables.Select(t => t.TableName).OrderBy(n => n).ToList()
                            };

                            // Index the schema
                            var schemaIndexer = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.RAG.SchemaIndexer>();
                            var indexResult = await schemaIndexer.IndexSchemaAsync(schema, fingerprint, id);

                            var indexingTime = DateTime.UtcNow - indexingStartTime;

                            if (indexResult.Success)
                            {
                                // Check if indexing was successful
                                var updatedCollectionExists = await qdrantService.CollectionExistsAsync();
                                if (updatedCollectionExists)
                                {
                                    var updatedCollectionInfo = await qdrantService.GetCollectionInfoAsync();
                                    result.SchemaIndexing.CollectionExists = true;
                                    result.SchemaIndexing.SchemasIndexed = (int)(updatedCollectionInfo?.PointsCount ?? 0);
                                    result.SchemaIndexing.AutoIndexed = true;
                                    result.SchemaIndexing.IndexingTime = indexingTime.TotalSeconds.ToString("F1") + "s";
                                }
                                else
                                {
                                    result.SchemaIndexing.ErrorMessage = "Schema indexing completed but collection was not created";
                                }
                            }
                            else
                            {
                                result.SchemaIndexing.ErrorMessage = indexResult.ErrorMessage ?? "Schema indexing failed";
                            }
                        } // ← DatabaseConfigContext auto-restores here via IDisposable
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[TestEnhanced] Schema indexing failed for {ConnectionId}", id);
                        result.SchemaIndexing.ErrorMessage = $"Schema indexing failed: {ex.Message}";
                    }
                }

                result.ReadyForChat = result.DatabaseConnection.Success && result.SchemaIndexing.SchemasIndexed > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking schema indexing for connection {ConnectionId}", id);
                result.SchemaIndexing = new SchemaIndexingResult
                {
                    CollectionExists = false,
                    SchemasIndexed = 0,
                    ErrorMessage = $"Schema indexing check failed: {ex.Message}"
                };
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"testing enhanced connection {id}");
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


    /// <summary>
    /// Set a connection as default
    /// </summary>
    [HttpPost("{id}/set-default")]
    public async Task<IActionResult> SetDefaultConnection(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();
            var success = await _connectionService.SetDefaultConnectionAsync(id, userId);

            if (!success)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            // ✅ PHASE 3: Trigger background schema pre-loading for faster queries
            if (_orchestrator != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("[Connections] Pre-loading schema for connection {Id}", id);
                        _orchestrator.ClearSchemaCache(); // Clear old cache first
                                                          // Schema will be loaded on first query, but this warms up the cache
                        _logger.LogInformation("[Connections] Schema cache cleared, ready for pre-loading");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Connections] Failed to pre-load schema for {Id}", id);
                    }
                });
            }

            return Ok(new { Message = "Connection set as default successfully" });
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"setting default connection {id}");
        }
    }

    /// <summary>
    /// Get schema for a connection
    /// </summary>
    [HttpGet("{id}/schema")]
    public async Task<IActionResult> GetConnectionSchema(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();
            var connection = await _connectionService.GetConnectionAsync(id, userId);

            if (connection == null)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            // TODO: Implement schema retrieval
            var schema = new
            {
                tables = new object[0],
                lastSyncedAt = (DateTime?)null,
                tableCount = 0,
                columnCount = 0
            };

            return Ok(schema);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"retrieving schema for connection {id}");
        }
    }

    /// <summary>
    /// Sync/refresh schema for a connection
    /// </summary>
    [HttpPost("{id}/sync")]
    public async Task<IActionResult> SyncConnectionSchema(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                return BadRequest(new { Message = "Invalid connection ID format" });
            }

            var userId = GetRequiredUserId();
            var connection = await _connectionService.GetConnectionAsync(id, userId);

            if (connection == null)
            {
                return NotFound(new { Message = "Connection not found" });
            }

            // Trigger actual schema synchronization
            var syncSuccess = await _connectionService.SyncSchemaAsync(id, userId);

            if (!syncSuccess)
            {
                return StatusCode(500, new { success = false, message = "Failed to sync schema" });
            }

            // Also trigger vector indexing
            var vectorSearchService = HttpContext.RequestServices.GetRequiredService<IVectorSearchService>();
            var indexSuccess = await vectorSearchService.IndexSchemaAsync(id);

            var result = new
            {
                success = syncSuccess && indexSuccess,
                message = indexSuccess ? "Schema synchronized and indexed successfully" : "Schema synchronized but indexing failed",
                syncedAt = DateTime.UtcNow,
                indexed = indexSuccess,
                tableCount = 0, // TODO: Get actual count from schema scanner
                columnCount = 0 // TODO: Get actual count from schema scanner
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"syncing schema for connection {id}");
        }
    }

    /// <summary>
    /// Refresh schema cache for a connection (user-triggered)
    /// </summary>
    [HttpPost("{id}/refresh-schema")]
    public async Task<ActionResult<SchemaRefreshResult>> RefreshSchema(string id)
    {
        try
        {
            var userId = GetRequiredUserId();

            // Verify connection exists and belongs to user
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(id, userId);
            if (connection == null)
            {
                return NotFound(new { error = "Connection not found" });
            }

            _logger.LogInformation("[RefreshSchema] Refreshing schema for connection {ConnectionId}", id);

            // Get connection string
            var connectionString = _encryptionService.GetConnectionString(connection);

            // ✅ CRIT-2 FIX: Use DatabaseConfigContext.SetConnectionString() instead of mutating Singleton
            using (DatabaseConfigContext.SetConnectionString(connectionString))
            {
                // Scan schema
                var schemaScanner = HttpContext.RequestServices.GetRequiredService<SchemaScanner>();
                var schema = await schemaScanner.ScanAsync();

                if (schema == null || schema.Tables.Count == 0)
                {
                    return BadRequest(new { error = "Failed to scan database schema" });
                }

                // Save to cache
                var schemaCache = HttpContext.RequestServices.GetRequiredService<ISchemaCache>();
                await schemaCache.SetAsync(id, schema);

                _logger.LogInformation("[RefreshSchema] Refreshed {TableCount} tables for connection {ConnectionId}",
                    schema.Tables.Count, id);

                return Ok(new SchemaRefreshResult
                {
                    Success = true,
                    TableCount = schema.Tables.Count,
                    RefreshedAt = DateTime.UtcNow
                });
            } // ← DatabaseConfigContext auto-restores here via IDisposable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RefreshSchema] Failed for connection {ConnectionId}", id);
            return HandleException(ex, "refreshing schema");
        }
    }

    /// <summary>
    /// Test a new connection (without saving)
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult<TestConnectionResult>> TestNewConnection([FromBody] CreateConnectionRequest request)
    {
        try
        {
            if (!IsModelValid())
            {
                return GetValidationErrors();
            }

            // TODO: Implement connection testing without saving
            var result = new TestConnectionResult
            {
                Success = true,
                DatabaseVersion = "Test Version",
                ResponseTime = TimeSpan.FromMilliseconds(100),
                TestedAt = DateTime.UtcNow
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "testing new connection");
        }
    }
}

/// <summary>
/// Enhanced test connection result with Qdrant collection check
/// </summary>
public class EnhancedTestConnectionResult
{
    public bool Success { get; set; }
    public DatabaseConnectionResult DatabaseConnection { get; set; } = new();
    public SchemaIndexingResult SchemaIndexing { get; set; } = new();
    public bool ReadyForChat { get; set; }
}

/// <summary>
/// Database connection test result
/// </summary>
public class DatabaseConnectionResult
{
    public bool Success { get; set; }
    public string ResponseTime { get; set; } = string.Empty;
    public string DatabaseVersion { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Schema indexing result
/// </summary>
public class SchemaIndexingResult
{
    public bool CollectionExists { get; set; }
    public int SchemasIndexed { get; set; }
    public bool AutoIndexed { get; set; }
    public string? IndexingTime { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Schema refresh result (user-triggered)
/// </summary>
public class SchemaRefreshResult
{
    public bool Success { get; set; }
    public int TableCount { get; set; }
    public DateTime RefreshedAt { get; set; }
}