using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;

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

    public ConnectionsController(
        IConnectionService connectionService,
        IUnitOfWork unitOfWork,
        IConnectionEncryptionService encryptionService,
        ILogger<ConnectionsController> logger) : base(logger)
    {
        _connectionService = connectionService;
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
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
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleException(ex, $"testing connection {id}");
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
                var connectionString = _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);
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
                        dbConfig.ConnectionString = connectionString;

                        // Scan database schema
                        var schemaScanner = HttpContext.RequestServices.GetRequiredService<TextToSqlAgent.Infrastructure.Database.SchemaScanner>();
                        var schema = await schemaScanner.ScanAsync();

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
                    }
                    finally
                    {
                        // Restore original connection string
                        dbConfig.ConnectionString = originalConnectionString;
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