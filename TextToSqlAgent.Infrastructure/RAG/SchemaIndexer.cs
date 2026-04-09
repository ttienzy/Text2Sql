using Microsoft.Extensions.Logging;
using System.Diagnostics;
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

    /// <summary>
    /// PHASE-2 TASK 2.1: Check if schema is already indexed by comparing fingerprints.
    /// Prevents redundant indexing on every request.
    /// </summary>
    public virtual async Task<bool> IsSchemaIndexedAsync(
        SchemaFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if collection exists first
            var collectionExists = await _vectorStore.CollectionExistsAsync(cancellationToken);
            if (!collectionExists)
            {
                _logger.LogDebug("[SchemaIndexer] Collection does not exist, schema not indexed");
                return false;
            }

            // Try to retrieve stored fingerprint
            var storedFingerprint = await _vectorStore.GetStoredFingerprintAsync(cancellationToken);
            if (storedFingerprint == null)
            {
                _logger.LogDebug("[SchemaIndexer] No stored fingerprint found, schema not indexed");
                return false;
            }

            // Compare fingerprints
            var isMatch = storedFingerprint.Hash == fingerprint.Hash &&
                          storedFingerprint.TableCount == fingerprint.TableCount &&
                          storedFingerprint.ColumnCount == fingerprint.ColumnCount;

            if (isMatch)
            {
                _logger.LogInformation(
                    "[SchemaIndexer] ✓ Schema already indexed (fingerprint match: {Hash})",
                    fingerprint.Hash.Substring(0, 8));
            }
            else
            {
                _logger.LogInformation(
                    "[SchemaIndexer] Schema fingerprint mismatch - re-indexing required");
            }

            return isMatch;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SchemaIndexer] Failed to check schema fingerprint, assuming not indexed");
            return false;
        }
    }

    public virtual async Task<ConnectionResult> IndexSchemaAsync(
        DatabaseSchema schema,
        SchemaFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return await IndexSchemaAsync(schema, fingerprint, null, cancellationToken);
    }

    public virtual async Task<ConnectionResult> IndexSchemaAsync(
        DatabaseSchema schema,
        SchemaFingerprint fingerprint,
        string? connectionId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ConnectionResult { Success = false };

        try
        {
            _logger.LogInformation("[SchemaIndexer] Starting schema indexing...");

            // 1. Ensure collection exists
            try
            {
                await _vectorStore.EnsureCollectionAsync(cancellationToken);
                _logger.LogDebug("[SchemaIndexer] Collection ensured");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaIndexer] Failed to ensure collection exists");
                result.ErrorMessage = $"Collection creation failed: {ex.Message}";
                return result;
            }

            // 2. Build documents
            List<SchemaDocument> documents;
            try
            {
                documents = BuildSchemaDocuments(schema, connectionId);
                _logger.LogDebug("[SchemaIndexer] Created {Count} documents", documents.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaIndexer] Failed to build schema documents");
                result.ErrorMessage = $"Schema scanning failed: {ex.Message}";
                return result;
            }

            // 3. Generate embeddings and create points
            List<VectorPoint> points;
            try
            {
                points = await GeneratePointsAsync(documents, cancellationToken);
                _logger.LogDebug("[SchemaIndexer] Generated {Count} embeddings", points.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaIndexer] Failed to generate embeddings");
                result.ErrorMessage = $"Embedding generation failed: {ex.Message}";
                return result;
            }

            // 4. Upsert to vector store
            try
            {
                await _vectorStore.UpsertPointsAsync(points, cancellationToken);
                _logger.LogDebug("[SchemaIndexer] Upserted {Count} points to vector store", points.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaIndexer] Failed to upsert points to vector store");
                result.ErrorMessage = $"Vector store operation failed: {ex.Message}";
                return result;
            }

            // 5. Store schema fingerprint
            try
            {
                await _vectorStore.StoreSchemaFingerprintAsync(fingerprint, cancellationToken);
                _logger.LogDebug("[SchemaIndexer] Stored schema fingerprint: {Hash}", fingerprint.Hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaIndexer] Failed to store schema fingerprint");
                result.ErrorMessage = $"Fingerprint storage failed: {ex.Message}";
                return result;
            }

            stopwatch.Stop();
            result.Success = true;
            result.IndexingPerformed = true;
            result.PointsIndexed = points.Count;
            result.IndexingDuration = stopwatch.Elapsed;

            _logger.LogInformation(
                "[SchemaIndexer] ✓ Schema indexing complete: {Count} points in {Duration}ms",
                points.Count,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[SchemaIndexer] Unexpected error during schema indexing");
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            result.IndexingDuration = stopwatch.Elapsed;
            return result;
        }
    }

    private List<SchemaDocument> BuildSchemaDocuments(DatabaseSchema schema, string? connectionId = null)
    {
        var documents = new List<SchemaDocument>();
        var pointId = 0;

        // ==========================================
        // 1. INDEX TABLES (with context-rich chunks)
        // ==========================================
        foreach (var table in schema.Tables)
        {
            var tableDoc = new SchemaDocument
            {
                Id = $"table_{pointId++}",
                Type = SchemaDocumentType.Table,
                Content = BuildTableContent(table, schema),
                Metadata = new Dictionary<string, string>
                {
                    ["type"] = "table",
                    ["table_name"] = table.TableName,
                    ["schema"] = table.Schema,
                    ["column_count"] = table.Columns.Count.ToString()
                }
            };

            // Add connectionId to metadata if provided
            if (!string.IsNullOrEmpty(connectionId))
            {
                tableDoc.Metadata["connection_id"] = connectionId;
            }

            documents.Add(tableDoc);

            // ==========================================
            // 2. INDEX COLUMNS (with parent table context)
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

                // Add connectionId to metadata if provided
                if (!string.IsNullOrEmpty(connectionId))
                {
                    columnDoc.Metadata["connection_id"] = connectionId;
                }

                documents.Add(columnDoc);
            }
        }

        // ==========================================
        // 3. INDEX RELATIONSHIPS (with semantic descriptions)
        // ==========================================
        foreach (var rel in schema.Relationships)
        {
            var relDoc = new SchemaDocument
            {
                Id = $"relationship_{pointId++}",
                Type = SchemaDocumentType.Relationship,
                Content = BuildRelationshipContent(rel, schema),
                Metadata = new Dictionary<string, string>
                {
                    ["type"] = "relationship",
                    ["from_table"] = rel.FromTable,
                    ["from_column"] = rel.FromColumn,
                    ["to_table"] = rel.ToTable,
                    ["to_column"] = rel.ToColumn
                }
            };

            // Add connectionId to metadata if provided
            if (!string.IsNullOrEmpty(connectionId))
            {
                relDoc.Metadata["connection_id"] = connectionId;
            }

            documents.Add(relDoc);
        }

        return documents;
    }

    /// <summary>
    /// Builds table-level chunk with context-rich information (Requirements 2.1, 2.4)
    /// Format: "Table: {name}, Description: {description}, Columns: {column_list}, Relationships: {fk_relationships}"
    /// </summary>
    private string BuildTableContent(TableInfo table, DatabaseSchema schema)
    {
        // Include all column names with types and constraints (PK, FK)
        var columnList = string.Join(", ", table.Columns.Select(c =>
        {
            var pk = c.IsPrimaryKey ? ", PK" : "";
            var fk = c.IsForeignKey ? ", FK" : "";
            return $"{c.ColumnName} ({c.DataType}{pk}{fk})";
        }));

        // Include all foreign key relationships involving the table
        var relationships = schema.Relationships
            .Where(r => r.FromTable == table.TableName || r.ToTable == table.TableName)
            .Select(r => $"{r.FromTable}.{r.FromColumn} → {r.ToTable}.{r.ToColumn}")
            .ToList();

        var relationshipText = relationships.Any()
            ? $", Relationships: {string.Join("; ", relationships)}"
            : "";

        // Use description or inferred purpose
        var description = table.Description ?? InferTablePurpose(table.TableName);

        return $"Table: {table.TableName}, Description: {description}, Columns: {columnList}{relationshipText}";
    }

    /// <summary>
    /// Builds column-level chunk with parent table context (Requirements 2.2, 2.5)
    /// Format: "Column: {table}.{column}, Description: {description}, Type: {data_type}, Table: {table_context}"
    /// </summary>
    private string BuildColumnContent(TableInfo table, ColumnInfo column)
    {
        // Use description or inferred purpose
        var description = column.Description ?? InferColumnPurpose(column.ColumnName);

        // Include parent table context
        var tableContext = table.Description ?? InferTablePurpose(table.TableName);

        return $"Column: {table.TableName}.{column.ColumnName}, Description: {description}, " +
               $"Type: {column.DataType}, Table: {table.TableName} ({tableContext})";
    }

    /// <summary>
    /// Builds relationship-level chunk with semantic description (Requirements 2.3, 2.6)
    /// Format: "Relationship: {source_table}.{source_column} → {target_table}.{target_column}, Meaning: {semantic_description}"
    /// </summary>
    private string BuildRelationshipContent(RelationshipInfo rel, DatabaseSchema schema)
    {
        return GenerateRelationshipDescription(rel, schema);
    }

    /// <summary>
    /// Infers table purpose from naming patterns (Requirement 2.7)
    /// </summary>
    private string InferTablePurpose(string tableName)
    {
        var lower = tableName.ToLower();

        // Handle common table name patterns
        if (lower.Contains("order")) return "stores order information";
        if (lower.Contains("customer")) return "stores customer data";
        if (lower.Contains("product")) return "stores product catalog";
        if (lower.Contains("user")) return "stores user accounts";
        if (lower.Contains("invoice")) return "stores invoice records";
        if (lower.Contains("payment")) return "stores payment transactions";
        if (lower.Contains("employee")) return "stores employee information";
        if (lower.Contains("department")) return "stores department data";
        if (lower.Contains("category")) return "stores category information";
        if (lower.Contains("inventory")) return "stores inventory data";

        // Generic description for unknown patterns
        return $"stores {tableName.ToLower()} data";
    }

    /// <summary>
    /// Infers column purpose from naming patterns (Requirement 2.7)
    /// </summary>
    private string InferColumnPurpose(string columnName)
    {
        var lower = columnName.ToLower();

        // Handle common column patterns
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

        // Generic description for unknown patterns
        return columnName.ToLower();
    }

    /// <summary>
    /// Generates semantic relationship description using naming patterns (Requirements 9.1, 9.2, 9.3, 9.6)
    /// </summary>
    private string GenerateRelationshipDescription(RelationshipInfo rel, DatabaseSchema schema)
    {
        var fromTable = rel.FromTable.ToLower();
        var toTable = rel.ToTable.ToLower();
        var fromCol = rel.FromColumn.ToLower();

        // Detect common patterns for one-to-many relationships
        // Pattern: Orders.CustomerId → Customers.Id
        if (fromCol.Contains(toTable.TrimEnd('s')) || fromCol.Contains("id"))
        {
            var singularFrom = SingularForm(fromTable);
            var singularTo = SingularForm(toTable);
            return $"Relationship: {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}, " +
                   $"Meaning: Each {singularFrom} belongs to a {singularTo}";
        }

        // Generic fallback template
        return $"Relationship: {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}, " +
               $"Meaning: {rel.FromTable} references {rel.ToTable} via {rel.FromColumn}";
    }

    /// <summary>
    /// Converts plural table names to singular form for relationship descriptions
    /// </summary>
    private string SingularForm(string tableName)
    {
        var lower = tableName.ToLower();

        // Handle common plural patterns
        if (lower.EndsWith("ies"))
            return lower.Substring(0, lower.Length - 3) + "y";
        if (lower.EndsWith("ses") || lower.EndsWith("xes") || lower.EndsWith("zes"))
            return lower.Substring(0, lower.Length - 2);
        if (lower.EndsWith("s"))
            return lower.Substring(0, lower.Length - 1);

        return lower;
    }


    private async Task<List<VectorPoint>> GeneratePointsAsync(
        List<SchemaDocument> documents,
        CancellationToken cancellationToken)
    {
        var points = new List<VectorPoint>();
        var totalDocs = documents.Count;
        var batchSize = 10;
        var batches = documents.Chunk(batchSize).ToList();
        var totalBatches = batches.Count;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "[SchemaIndexer] Starting embedding generation: {Total} documents in {Batches} batches",
            totalDocs, totalBatches);

        // ✅ Print clear header to console
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Indexing schema: {totalDocs} documents ({totalBatches} batches)");
        Console.ResetColor();

        var completedDocs = 0;
        var isInteractive = !Console.IsOutputRedirected;

        for (int i = 0; i < totalBatches; i++)
        {
            var batch = batches[i].ToList();

            foreach (var doc in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var embedding = await _embeddingClient.GenerateEmbeddingAsync(doc.Content, cancellationToken);

                var payload = new Dictionary<string, object>
                {
                    ["id"] = doc.Id,
                    ["type"] = doc.Type.ToString(),
                    ["content"] = doc.Content
                };

                foreach (var (key, value) in doc.Metadata)
                    payload[key] = value;

                points.Add(new VectorPoint
                {
                    Id = doc.Id,
                    Vector = embedding,
                    Payload = payload
                });

                completedDocs++;

                // ✅ Progress bar after each document
                PrintProgress(completedDocs, totalDocs, stopwatch.Elapsed, isInteractive);
            }

            // ✅ Rate limit delay per batch (not per doc)
            if (i < totalBatches - 1)
            {
                _logger.LogDebug("[SchemaIndexer] Rate limit pause between batches...");
                await Task.Delay(500, cancellationToken);
            }
        }

        stopwatch.Stop();

        // ✅ Done line
        Console.WriteLine(); // new line after progress bar
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ Indexed {points.Count} documents in {stopwatch.Elapsed.TotalSeconds:F1}s");
        Console.ResetColor();

        _logger.LogInformation(
            "[SchemaIndexer] Generated {Count} embeddings in {Duration}ms",
            points.Count, stopwatch.ElapsedMilliseconds);

        return points;
    }

    private static void PrintProgress(int completed, int total, TimeSpan elapsed, bool isInteractive)
    {
        var percent = (double)completed / total;
        var barWidth = 30;
        var filled = (int)(barWidth * percent);
        var bar = new string('█', filled) + new string('░', barWidth - filled);

        // ✅ Calculate ETA
        var etaStr = completed > 0
            ? $"ETA {TimeSpan.FromSeconds(elapsed.TotalSeconds / completed * (total - completed)):mm\\:ss}"
            : "ETA --:--";

        if (isInteractive)
        {
            // ✅ \r to overwrite current line (no log spam)
            Console.Write($"\r  [{bar}] {completed}/{total} ({percent:P0})  {etaStr}  ");
        }
        else if (completed % 10 == 0) // Fallback: log every 10 docs
        {
            Console.WriteLine($"  Progress: {completed}/{total} ({percent:P0})");
        }
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
