using Microsoft.Extensions.Logging;
using Qdrant.Client.Grpc;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Infrastructure.RAG;

public class SchemaIndexer
{
    private readonly QdrantService _qdrant;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<SchemaIndexer> _logger;

    public SchemaIndexer(
        QdrantService qdrant,
        IEmbeddingClient embeddingClient,
        ILogger<SchemaIndexer> logger)
    {
        _qdrant = qdrant;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task IndexSchemaAsync(
        DatabaseSchema schema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Schema Indexer] Bắt đầu index schema...");

        // 1. Ensure collection exists with correct vector size (will recreate if size mismatch)
        await _qdrant.EnsureCollectionAsync(cancellationToken);

        // 2. Build documents
        var documents = BuildSchemaDocuments(schema);
        _logger.LogInformation("[Schema Indexer] Tạo {Count} documents", documents.Count);

        // 3. Generate embeddings
        var points = await GeneratePointsAsync(documents, cancellationToken);

        // 4. Upsert to Qdrant
        await _qdrant.UpsertPointsAsync(points, cancellationToken);

        _logger.LogInformation("[Schema Indexer] Hoàn tất index schema");
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

    private async Task<List<PointStruct>> GeneratePointsAsync(
        List<SchemaDocument> documents,
        CancellationToken cancellationToken)
    {
        var points = new List<PointStruct>();

        _logger.LogInformation("[Schema Indexer] Generating embeddings for {Count} documents", documents.Count);

        var batchSize = 10;
        var batches = documents.Chunk(batchSize).ToList();
        
        ulong pointId = 1; // Start from 1 for numeric IDs

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            _logger.LogInformation("[Schema Indexer] Processing batch {Current}/{Total}", i + 1, batches.Count);

            foreach (var doc in batch)
            {
                var embedding = await _embeddingClient.GenerateEmbeddingAsync(doc.Content, cancellationToken);

                var point = new PointStruct
                {
                    Id = new PointId { Num = pointId++ }, // Use numeric ID for REST API compatibility
                    Vectors = new Vectors { Vector = new Vector { Data = { embedding } } },
                    Payload =
                    {
                        ["id"] = doc.Id,
                        ["type"] = doc.Type.ToString(),
                        ["content"] = doc.Content
                    }
                };

                // Add metadata
                foreach (var (key, value) in doc.Metadata)
                {
                    point.Payload[key] = value;
                }

                points.Add(point);

                // Rate limiting - 500ms delay (safe for 60 RPM limit)
                await Task.Delay(500, cancellationToken);
            }
        }

        _logger.LogInformation("[Schema Indexer] Generated {Count} embeddings", points.Count);

        return points;
    }

    public async Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Schema Indexer] Clearing index...");

        var exists = await _qdrant.CollectionExistsAsync(cancellationToken);
        if (exists)
        {
            await _qdrant.DeleteCollectionAsync(cancellationToken);
        }

        _logger.LogInformation("[Schema Indexer] Index cleared");
    }
}
