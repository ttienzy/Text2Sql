using System.Diagnostics;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service for vector-based schema search and retrieval
/// </summary>
public class VectorSearchService : IVectorSearchService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly SchemaRetriever _schemaRetriever;
    private readonly SchemaIndexer _schemaIndexer;
    private readonly SchemaScanner _schemaScanner;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly IDatabaseAdapter _databaseAdapter;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        IUnitOfWork unitOfWork,
        SchemaRetriever schemaRetriever,
        SchemaIndexer schemaIndexer,
        SchemaScanner schemaScanner,
        IConnectionEncryptionService encryptionService,
        IDatabaseAdapter databaseAdapter,
        IVectorStore vectorStore,
        ILogger<VectorSearchService> logger)
    {
        _unitOfWork = unitOfWork;
        _schemaRetriever = schemaRetriever;
        _schemaIndexer = schemaIndexer;
        _schemaScanner = schemaScanner;
        _encryptionService = encryptionService;
        _databaseAdapter = databaseAdapter;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <summary>
    /// Search for relevant database schema based on natural language query
    /// </summary>
    public async Task<SchemaSearchResult> SearchSchemaAsync(string query, string connectionId, int maxResults = 10)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Verify connection exists
            var connection = await _unitOfWork.Connections.GetByIdAsync(connectionId);
            if (connection == null)
            {
                return new SchemaSearchResult
                {
                    Success = false,
                    ErrorMessage = "Connection not found",
                    SearchTime = stopwatch.Elapsed
                };
            }

            // Create a dummy schema for now - in real implementation, this would come from database scanning
            var dummySchema = new DatabaseSchema
            {
                Tables = new List<TableInfo>(),
                Relationships = new List<RelationshipInfo>()
            };

            // Use schema retriever to find relevant schema
            var schemaContext = await _schemaRetriever.RetrieveAsync(query, dummySchema, connectionId);

            stopwatch.Stop();

            var matches = new List<SchemaMatch>();

            // Convert schema context to matches
            if (schemaContext?.RelevantTables != null)
            {
                foreach (var table in schemaContext.RelevantTables)
                {
                    matches.Add(new SchemaMatch
                    {
                        TableName = table.TableName,
                        SchemaName = table.Schema ?? "dbo",
                        MatchType = "table",
                        Similarity = 0.8 // Placeholder - would come from vector search
                    });

                    if (table.Columns != null)
                    {
                        foreach (var column in table.Columns)
                        {
                            matches.Add(new SchemaMatch
                            {
                                TableName = table.TableName,
                                SchemaName = table.Schema ?? "dbo",
                                ColumnName = column.ColumnName,
                                DataType = column.DataType,
                                MatchType = "column",
                                Similarity = 0.7 // Placeholder
                            });
                        }
                    }
                }
            }

            return new SchemaSearchResult
            {
                Success = true,
                Matches = matches.Take(maxResults).ToList(),
                SearchTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error searching schema for connection {ConnectionId}", connectionId);

            return new SchemaSearchResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SearchTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Index database schema for vector search
    /// </summary>
    public async Task<bool> IndexSchemaAsync(string connectionId)
    {
        try
        {
            var connection = await _unitOfWork.Connections.GetByIdAsync(connectionId);
            if (connection == null)
            {
                _logger.LogWarning("Connection {ConnectionId} not found for schema indexing", connectionId);
                return false;
            }

            // Get schema scanner
            var schemaScanner = _schemaScanner;

            // Decrypt password and build connection string
            var decryptedPassword = _encryptionService.DecryptPassword(connection.EncryptedPassword, connection.Id);
            var connectionString = $"Server={connection.Host};Database={connection.Database};User Id={connection.Username};Password={decryptedPassword};TrustServerCertificate=True;";

            _logger.LogInformation("Scanning schema for connection {ConnectionId} to database {DatabaseName}", connectionId, connection.Database);

            // Create a temporary database config for this connection
            var tempConfig = new DatabaseConfig { ConnectionString = connectionString };
            var scannerLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SchemaScanner>.Instance;
            var tempScanner = new SchemaScanner(tempConfig, _databaseAdapter, scannerLogger);

            // Scan actual schema from database
            var schema = await tempScanner.ScanAsync();

            if (schema == null || !schema.Tables.Any())
            {
                _logger.LogWarning("No schema found for connection {ConnectionId}", connectionId);
                return false;
            }

            // Create fingerprint from actual schema
            var fingerprint = new SchemaFingerprint
            {
                Hash = ComputeSchemaHash(schema),
                ComputedAt = DateTime.UtcNow,
                TableCount = schema.Tables.Count,
                ColumnCount = schema.Tables.Sum(t => t.Columns.Count),
                RelationshipCount = schema.Relationships.Count,
                TableNames = schema.Tables.Select(t => t.TableName).ToList()
            };

            _logger.LogInformation("Schema scanned for connection {ConnectionId}: {TableCount} tables, {ColumnCount} columns",
                connectionId, fingerprint.TableCount, fingerprint.ColumnCount);

            // Use schema indexer to index the schema
            var result = await _schemaIndexer.IndexSchemaAsync(schema, fingerprint, connectionId);

            _logger.LogInformation("Schema indexing result for connection {ConnectionId}: {Success}", connectionId, result.Success);
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing schema for connection {ConnectionId}", connectionId);
            return false;
        }
    }

    private string ComputeSchemaHash(DatabaseSchema schema)
    {
        var content = string.Join("|",
            schema.Tables.OrderBy(t => t.TableName)
                .Select(t => $"{t.TableName}:{string.Join(",", t.Columns.OrderBy(c => c.ColumnName).Select(c => $"{c.ColumnName}:{c.DataType}"))}"));

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Get schema indexing status
    /// </summary>
    public async Task<SchemaIndexStatus> GetIndexStatusAsync(string connectionId)
    {
        try
        {
            // Check if connection exists
            var connection = await _unitOfWork.Connections.GetByIdAsync(connectionId);
            if (connection == null)
            {
                return new SchemaIndexStatus
                {
                    IsIndexed = false,
                    LastError = "Connection not found"
                };
            }

            // For now, return a placeholder status
            // In real implementation, this would check the vector store for indexed data
            return new SchemaIndexStatus
            {
                IsIndexed = false, // TODO: Check actual indexing status
                LastIndexedAt = null,
                TableCount = 0,
                ColumnCount = 0,
                IsIndexing = false,
                LastError = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting index status for connection {ConnectionId}", connectionId);

            return new SchemaIndexStatus
            {
                IsIndexed = false,
                LastError = ex.Message
            };
        }
    }

    /// <summary>
    /// Refresh schema index for a connection
    /// </summary>
    public async Task<bool> RefreshIndexAsync(string connectionId)
    {
        try
        {
            // Check if connection exists
            var connection = await _unitOfWork.Connections.GetByIdAsync(connectionId);
            if (connection == null)
            {
                return false;
            }

            // For now, just call IndexSchemaAsync
            // In real implementation, this would clear existing data first
            return await IndexSchemaAsync(connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing index for connection {ConnectionId}", connectionId);
            return false;
        }
    }

    /// <summary>
    /// Search for similar queries based on vector similarity
    /// </summary>
    public async Task<IEnumerable<SimilarQuery>> FindSimilarQueriesAsync(string query, string userId, int maxResults = 5)
    {
        try
        {
            // TODO: Implement query similarity search using vector store
            // This would require storing successful queries with their embeddings

            _logger.LogInformation("Searching for similar queries for user {UserId}", userId);

            // Placeholder implementation
            return new List<SimilarQuery>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar queries for user {UserId}", userId);
            return new List<SimilarQuery>();
        }
    }

    /// <summary>
    /// Store a successful query for future similarity search
    /// </summary>
    public async Task StoreQueryAsync(string query, string sqlQuery, string userId, string connectionId)
    {
        try
        {
            // TODO: Store query with embedding for similarity search
            // This would involve:
            // 1. Generate embedding for the natural language query
            // 2. Store in vector database with metadata (sql, user, connection)

            _logger.LogInformation("Storing query for future similarity search: {Query}", query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing query for similarity search");
        }
    }
}