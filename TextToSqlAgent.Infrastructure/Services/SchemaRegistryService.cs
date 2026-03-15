using System.Data.Common;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Infrastructure.Services;

/// <summary>
/// Service for managing database schema registry with caching support
/// </summary>
public interface ISchemaRegistryService
{
    /// <summary>
    /// Syncs schema for a specific connection
    /// </summary>
    Task<SchemaSyncResult> SyncSchemaAsync(string connectionId, string connectionString, string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached schema for a connection
    /// </summary>
    Task<SchemaSummary> GetSchemaSummaryAsync(string connectionId);

    /// <summary>
    /// Gets detailed schema information for a connection
    /// </summary>
    Task<List<SchemaTableInfo>> GetSchemaDetailsAsync(string connectionId);

    /// <summary>
    /// Clears cached schema for a connection
    /// </summary>
    Task ClearCacheAsync(string connectionId);
}

/// <summary>
/// Result of a schema sync operation
/// </summary>
public class SchemaSyncResult
{
    public bool Success { get; set; }
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public DateTime SyncedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Summary of schema for a connection
/// </summary>
public class SchemaSummary
{
    public string ConnectionId { get; set; } = string.Empty;
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public List<string> Tables { get; set; } = new();
}

/// <summary>
/// Detailed table information
/// </summary>
public class SchemaTableInfo
{
    public string TableName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "dbo";
    public List<SchemaColumnInfo> Columns { get; set; } = new();
}

/// <summary>
/// Column information
/// </summary>
public class SchemaColumnInfo
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public string? ReferencedTable { get; set; }
    public string? ReferencedColumn { get; set; }
}

/// <summary>
/// Schema Registry implementation
/// </summary>
public class SchemaRegistryService : ISchemaRegistryService
{
    private readonly ILogger<SchemaRegistryService> _logger;
    private readonly Dictionary<string, DatabaseSchema> _schemaCache = new();
    private readonly Dictionary<string, DateTime> _cacheTimestamps = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
    private readonly QdrantService? _qdrantService;
    private readonly IEmbeddingClient? _embeddingClient;

    public SchemaRegistryService(
        ILogger<SchemaRegistryService> logger,
        QdrantService? qdrantService = null,
        IEmbeddingClient? embeddingClient = null)
    {
        _logger = logger;
        _qdrantService = qdrantService;
        _embeddingClient = embeddingClient;
    }

    /// <inheritdoc />
    public async Task<SchemaSyncResult> SyncSchemaAsync(string connectionId, string connectionString, string provider, CancellationToken cancellationToken = default)
    {
        var result = new SchemaSyncResult { SyncedAt = DateTime.UtcNow };

        try
        {
            _logger.LogInformation("Starting schema sync for connection {ConnectionId}", connectionId);

            // Get the appropriate adapter for the provider
            var adapter = GetAdapterForProvider(provider);

            using var connection = adapter.CreateConnection(connectionString);

            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }

            var schema = await adapter.GetSchemaAsync(connection, cancellationToken);

            // Store in cache
            _schemaCache[connectionId] = schema;
            _cacheTimestamps[connectionId] = DateTime.UtcNow;

            // ✅ NEW: Upload schema to Qdrant for semantic search
            if (_qdrantService != null && _embeddingClient != null)
            {
                try
                {
                    _logger.LogInformation("Uploading schema to Qdrant for connection {ConnectionId}...", connectionId);
                    _qdrantService.SetCollectionName(connectionId);

                    var schemaElements = new List<(string Name, string Type, string? Table, string? Description)>();

                    foreach (var table in schema.Tables)
                    {
                        // Add table entry
                        var tableDescription = $"Table: {table.TableName}, Columns: {string.Join(", ", table.Columns.Select(c => c.ColumnName))}";
                        schemaElements.Add((table.TableName, "table", null, tableDescription));

                        // Add column entries
                        foreach (var column in table.Columns)
                        {
                            var columnDescription = $"Table: {table.TableName}, Column: {column.ColumnName}, Type: {column.DataType}";
                            schemaElements.Add((column.ColumnName, "column", table.TableName, columnDescription));
                        }
                    }

                    if (schemaElements.Any())
                    {
                        await _qdrantService.UpsertSchemaElementsAsync(schemaElements, _embeddingClient, cancellationToken);
                        _logger.LogInformation("Successfully uploaded {PointCount} schema points to Qdrant", schemaElements.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload schema to Qdrant - continuing without semantic search");
                }
            }
            else
            {
                _logger.LogWarning("QdrantService or EmbeddingClient not available - skipping schema upload to Qdrant");
            }

            result.Success = true;
            result.TableCount = schema.Tables.Count;
            result.ColumnCount = schema.Tables.Sum(t => t.Columns.Count);

            _logger.LogInformation("Schema sync complete for connection {ConnectionId}: {TableCount} tables, {ColumnCount} columns",
                connectionId, result.TableCount, result.ColumnCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync schema for connection {ConnectionId}", connectionId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <inheritdoc />
    public Task<SchemaSummary> GetSchemaSummaryAsync(string connectionId)
    {
        if (!_schemaCache.TryGetValue(connectionId, out var schema))
        {
            return Task.FromResult(new SchemaSummary { ConnectionId = connectionId });
        }

        var summary = new SchemaSummary
        {
            ConnectionId = connectionId,
            TableCount = schema.Tables.Count,
            ColumnCount = schema.Tables.Sum(t => t.Columns.Count),
            LastSyncedAt = _cacheTimestamps.TryGetValue(connectionId, out var timestamp) ? timestamp : null,
            Tables = schema.Tables.Select(t => t.TableName).ToList()
        };

        return Task.FromResult(summary);
    }

    /// <inheritdoc />
    public Task<List<SchemaTableInfo>> GetSchemaDetailsAsync(string connectionId)
    {
        if (!_schemaCache.TryGetValue(connectionId, out var schema))
        {
            return Task.FromResult(new List<SchemaTableInfo>());
        }

        var details = schema.Tables.Select(t => new SchemaTableInfo
        {
            TableName = t.TableName,
            SchemaName = t.Schema,
            Columns = t.Columns.Select(c => new SchemaColumnInfo
            {
                ColumnName = c.ColumnName,
                DataType = c.DataType,
                IsNullable = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey,
                IsForeignKey = c.IsForeignKey
            }).ToList()
        }).ToList();

        return Task.FromResult(details);
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(string connectionId)
    {
        _schemaCache.Remove(connectionId);
        _cacheTimestamps.Remove(connectionId);
        _logger.LogInformation("Cleared schema cache for connection {ConnectionId}", connectionId);
        return Task.CompletedTask;
    }

    private IDatabaseAdapter GetAdapterForProvider(string provider)
    {
        // For simplicity, return a basic adapter based on provider
        // In a real implementation, this would use the DI container or factory
        return provider.ToLowerInvariant() switch
        {
            "sqlserver" => new TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>.Instance),
            _ => throw new NotSupportedException($"Provider {provider} is not supported for schema sync")
        };
    }
}
