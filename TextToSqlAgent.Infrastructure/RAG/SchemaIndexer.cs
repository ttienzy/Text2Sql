using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Schema indexer using abstracted vector store
/// Supports any IVectorStore implementation (Qdrant, in-memory, etc.)
/// </summary>
public class SchemaIndexer
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<SchemaIndexer> _logger;

    public SchemaIndexer(
        IVectorStore vectorStore,
        IEmbeddingClient embeddingClient,
        ILogger<SchemaIndexer> logger)
    {
        _vectorStore = vectorStore;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task IndexSchemaAsync(
        DatabaseSchema schema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SchemaIndexer] Starting schema indexing...");

        // 1. Ensure collection exists
        await _vectorStore.EnsureCollectionAsync(cancellationToken);

        // 2. Build documents
        var documents = BuildSchemaDocuments(schema);
        _logger.LogDebug("[SchemaIndexer] Created {Count} documents", documents.Count);

        // 3. Generate embeddings and create points
        var points = await GeneratePointsAsync(documents, cancellationToken);

        // 4. Upsert to vector store
        await _vectorStore.UpsertPointsAsync(points, cancellationToken);

        _logger.LogInformation("[SchemaIndexer] ✓ Schema indexing complete: {Count} points", points.Count);
    }

    private List<SchemaDocument> BuildSchemaDocuments(DatabaseSchema schema)
    {
        var documents = new List<SchemaDocument>();
        var pointId = 0;

        // ==========================================
        // 1. INDEX TABLES
        // ==========================================
        foreach (var table in schema.Tables)
        {
            var tableDoc = new SchemaDocument
            {
                Id = $"table_{pointId++}",
                Type = SchemaDocumentType.Table,
                Content = BuildTableContent(table),
                Metadata = new Dictionary<string, string>
                {
                    ["type"] = "table",
                    ["table_name"] = table.TableName,
                    ["schema"] = table.Schema,
                    ["column_count"] = table.Columns.Count.ToString()
                }
            };

            documents.Add(tableDoc);

            // ==========================================
            // 2. INDEX COLUMNS
            // ==========================================
            foreach (var column in table.Columns)
            {
                var columnDoc = new SchemaDocument
                {
                    Id = $"column_{pointId++}",
                    Type = SchemaDocumentType.Column,
                    Content = BuildColumnContent(table, column),
                    Metadata = new Dictionary<string, string>
                    {
                        ["type"] = "column",
                        ["table_name"] = table.TableName,
                        ["column_name"] = column.ColumnName,
                        ["data_type"] = column.DataType,
                        ["is_primary_key"] = column.IsPrimaryKey.ToString(),
                        ["is_foreign_key"] = column.IsForeignKey.ToString()
                    }
                };

                documents.Add(columnDoc);
            }
        }

        // ==========================================
        // 3. INDEX RELATIONSHIPS
        // ==========================================
        foreach (var rel in schema.Relationships)
        {
            var relDoc = new SchemaDocument
            {
                Id = $"relationship_{pointId++}",
                Type = SchemaDocumentType.Relationship,
                Content = BuildRelationshipContent(rel),
                Metadata = new Dictionary<string, string>
                {
                    ["type"] = "relationship",
                    ["from_table"] = rel.FromTable,
                    ["from_column"] = rel.FromColumn,
                    ["to_table"] = rel.ToTable,
                    ["to_column"] = rel.ToColumn
                }
            };

            documents.Add(relDoc);
        }

        return documents;
    }

    private string BuildTableContent(TableInfo table)
    {
        // Format: "Table Customers in schema dbo has columns: Id (int, PK), Name (nvarchar), Email (nvarchar)"
        var columnList = string.Join(", ", table.Columns.Select(c =>
        {
            var pk = c.IsPrimaryKey ? ", PK" : "";
            var fk = c.IsForeignKey ? ", FK" : "";
            return $"{c.ColumnName} ({c.DataType}{pk}{fk})";
        }));

        return $"Table {table.TableName} in schema {table.Schema} has columns: {columnList}";
    }

    private string BuildColumnContent(TableInfo table, ColumnInfo column)
    {
        // Format: "Column Name in table Customers is of type nvarchar and stores customer name"
        var purpose = GuessColumnPurpose(column.ColumnName);
        var pkInfo = column.IsPrimaryKey ? " (Primary Key)" : "";
        var fkInfo = column.IsForeignKey ? " (Foreign Key)" : "";

        return $"Column {column.ColumnName} in table {table.TableName} is of type {column.DataType}{pkInfo}{fkInfo} and stores {purpose}";
    }

    private string BuildRelationshipContent(RelationshipInfo rel)
    {
        // Format: "Orders.CustomerId references Customers.Id, linking orders to their customer"
        return $"{rel.FromTable}.{rel.FromColumn} references {rel.ToTable}.{rel.ToColumn}, linking {rel.FromTable.ToLower()} to {rel.ToTable.ToLower()}";
    }

    private string GuessColumnPurpose(string columnName)
    {
        // Simple heuristics to make embeddings more meaningful
        var lower = columnName.ToLower();

        if (lower == "id") return "unique identifier";
        if (lower.Contains("name")) return "name information";
        if (lower.Contains("email")) return "email address";
        if (lower.Contains("phone")) return "phone number";
        if (lower.Contains("address")) return "address information";
        if (lower.Contains("date")) return "date information";
        if (lower.Contains("amount") || lower.Contains("price")) return "monetary value";
        if (lower.Contains("quantity") || lower.Contains("count")) return "quantity or count";
        if (lower.Contains("status")) return "status information";
        if (lower.Contains("description")) return "description text";

        return columnName.ToLower();
    }

    private async Task<List<VectorPoint>> GeneratePointsAsync(
        List<SchemaDocument> documents,
        CancellationToken cancellationToken)
    {
        var points = new List<VectorPoint>();

        _logger.LogInformation("[SchemaIndexer] Generating embeddings for {Count} documents", documents.Count);

        var batchSize = 10;
        var batches = documents.Chunk(batchSize).ToList();

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            _logger.LogDebug("[SchemaIndexer] Processing batch {Current}/{Total}", i + 1, batches.Count);

            foreach (var doc in batch)
            {
                var embedding = await _embeddingClient.GenerateEmbeddingAsync(doc.Content, cancellationToken);

                var payload = new Dictionary<string, object>
                {
                    ["id"] = doc.Id,
                    ["type"] = doc.Type.ToString(),
                    ["content"] = doc.Content
                };

                // Add metadata
                foreach (var (key, value) in doc.Metadata)
                {
                    payload[key] = value;
                }

                var point = new VectorPoint
                {
                    Id = doc.Id,
                    Vector = embedding,
                    Payload = payload
                };

                points.Add(point);

                // Rate limiting - 500ms delay (safe for 60 RPM limit)
                await Task.Delay(500, cancellationToken);
            }
        }

        _logger.LogDebug("[SchemaIndexer] Generated {Count} embeddings", points.Count);

        return points;
    }

    public async Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SchemaIndexer] Clearing index...");

        var exists = await _vectorStore.CollectionExistsAsync(cancellationToken);
        if (exists)
        {
            await _vectorStore.DeleteCollectionAsync(cancellationToken);
        }

        _logger.LogInformation("[SchemaIndexer] Index cleared");
    }
}
