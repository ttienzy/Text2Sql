using Microsoft.Extensions.Logging;
using Qdrant.Client.Grpc;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models.DbExplorer;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Indexes DB Explorer data (tables + semantic tags) into Qdrant for semantic search
/// </summary>
public class DbExplorerQdrantIndexer
{
    private readonly QdrantService _qdrantService;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly SemanticTagGenerator _tagGenerator;
    private readonly ILogger<DbExplorerQdrantIndexer> _logger;

    public DbExplorerQdrantIndexer(
        QdrantService qdrantService,
        IEmbeddingClient embeddingClient,
        SemanticTagGenerator tagGenerator,
        ILogger<DbExplorerQdrantIndexer> logger)
    {
        _qdrantService = qdrantService;
        _embeddingClient = embeddingClient;
        _tagGenerator = tagGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Index database schema with semantic tags into Qdrant
    /// </summary>
    public async Task IndexSchemaWithSemanticTagsAsync(
        EnhancedDatabaseSchema schema,
        string? systemContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DbExplorerQdrantIndexer] Starting indexing for {TableCount} tables",
            schema.EnhancedTables.Count);

        try
        {
            // 1. Generate semantic tags for all tables (batch)
            var semanticTagsMap = await _tagGenerator.GenerateTagsBatchAsync(
                schema.EnhancedTables,
                systemContext,
                batchSize: 10,
                cancellationToken);

            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] Generated semantic tags for {Count} tables",
                semanticTagsMap.Count);

            // 2. Build text representations for embedding
            var tableTexts = new List<string>();
            var tableMetadata = new List<EnhancedTableInfo>();

            foreach (var table in schema.EnhancedTables)
            {
                if (!semanticTagsMap.TryGetValue(table.TableName, out var tags))
                {
                    _logger.LogWarning(
                        "[DbExplorerQdrantIndexer] No tags found for table {TableName}, skipping",
                        table.TableName);
                    continue;
                }

                // Build rich text for embedding: table name + role + module + semantic tags
                var textParts = new List<string>
                {
                    $"Table: {table.TableName}",
                    $"Role: {table.Role}",
                    $"Module: {table.Module ?? "Unknown"}",
                    $"Columns: {table.ColumnCount}",
                    $"Rows: {table.RowCount:N0}"
                };

                // Add semantic tags
                if (tags.Vietnamese.Any())
                {
                    textParts.Add($"Vietnamese: {string.Join(", ", tags.Vietnamese)}");
                }

                if (tags.English.Any())
                {
                    textParts.Add($"English: {string.Join(", ", tags.English)}");
                }

                if (tags.Abbreviations.Any())
                {
                    textParts.Add($"Abbreviations: {string.Join(", ", tags.Abbreviations)}");
                }

                if (tags.RelatedConcepts.Any())
                {
                    textParts.Add($"Related: {string.Join(", ", tags.RelatedConcepts)}");
                }

                var text = string.Join(" | ", textParts);
                tableTexts.Add(text);
                tableMetadata.Add(table);
            }

            if (tableTexts.Count == 0)
            {
                _logger.LogWarning("[DbExplorerQdrantIndexer] No tables to index");
                return;
            }

            // 3. Generate embeddings
            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] Generating embeddings for {Count} tables",
                tableTexts.Count);

            var embeddings = await _embeddingClient.GenerateBatchEmbeddingsAsync(
                tableTexts,
                cancellationToken);

            // 4. Build Qdrant points
            var points = new List<PointStruct>();
            for (int i = 0; i < tableMetadata.Count; i++)
            {
                var table = tableMetadata[i];
                var embedding = embeddings[i];
                var tags = semanticTagsMap[table.TableName];

                var point = new PointStruct
                {
                    Id = new PointId { Num = (ulong)(i + 1) },
                    Vectors = new Vectors
                    {
                        Vector = new Vector
                        {
                            Data = { embedding }
                        }
                    },
                    Payload =
                    {
                        ["type"] = new Value { StringValue = "table" },
                        ["table_name"] = new Value { StringValue = table.TableName },
                        ["role"] = new Value { StringValue = table.Role?.ToString() ?? "Unknown" },
                        ["module"] = new Value { StringValue = table.Module ?? "Unknown" },
                        ["column_count"] = new Value { IntegerValue = table.ColumnCount },
                        ["row_count"] = new Value { IntegerValue = table.RowCount },
                        ["semantic_tags"] = new Value { StringValue = string.Join(", ", tags.AllTags) },
                        ["vietnamese_tags"] = new Value { StringValue = string.Join(", ", tags.Vietnamese) },
                        ["english_tags"] = new Value { StringValue = string.Join(", ", tags.English) },
                        ["abbreviations"] = new Value { StringValue = string.Join(", ", tags.Abbreviations) },
                        ["related_concepts"] = new Value { StringValue = string.Join(", ", tags.RelatedConcepts) }
                    }
                };

                points.Add(point);
            }

            // 5. Upsert to Qdrant
            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] Upserting {Count} points to Qdrant",
                points.Count);

            await _qdrantService.UpsertPointsAsync(points, cancellationToken);

            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] ✅ Successfully indexed {Count} tables with semantic tags",
                points.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[DbExplorerQdrantIndexer] Failed to index schema with semantic tags");
            throw;
        }
    }

    /// <summary>
    /// Search tables by semantic query
    /// </summary>
    public async Task<List<TableSearchResult>> SearchTablesAsync(
        string query,
        int limit = 10,
        double scoreThreshold = 0.7,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Starting search with query: '{Query}', limit: {Limit}, threshold: {Threshold}",
            query, limit, scoreThreshold);

        try
        {
            // 1. Generate embedding for query
            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Generating embedding for query...");

            var queryEmbedding = await _embeddingClient.GenerateEmbeddingAsync(
                query,
                cancellationToken);

            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Embedding generated, dimension: {Dimension}",
                queryEmbedding.Length);

            // 2. Search Qdrant
            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Calling Qdrant search...");

            var results = await _qdrantService.SearchAsync(
                queryEmbedding,
                (ulong)limit,
                scoreThreshold,
                cancellationToken);

            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Qdrant returned {Count} results",
                results.Count);

            // 3. Convert to TableSearchResult
            var searchResults = results.Select(r => new TableSearchResult
            {
                TableName = r.Payload.TryGetValue("table_name", out var tableName)
                    ? tableName.StringValue
                    : "Unknown",
                Role = r.Payload.TryGetValue("role", out var role)
                    ? role.StringValue
                    : "Unknown",
                Module = r.Payload.TryGetValue("module", out var module)
                    ? module.StringValue
                    : "Unknown",
                Score = r.Score,
                SemanticTags = r.Payload.TryGetValue("semantic_tags", out var tags)
                    ? tags.StringValue.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList()
                    : new List<string>()
            }).ToList();

            _logger.LogInformation(
                "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Converted to {Count} TableSearchResult objects",
                searchResults.Count);

            if (searchResults.Any())
            {
                _logger.LogInformation(
                    "[DbExplorerQdrantIndexer] 🔍 SEARCH DEBUG - Sample result: Table='{TableName}', Score={Score}",
                    searchResults.First().TableName, searchResults.First().Score);
            }

            return searchResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[DbExplorerQdrantIndexer] Failed to search tables");
            throw;
        }
    }
}

/// <summary>
/// Table search result with semantic tags
/// </summary>
public class TableSearchResult
{
    public string TableName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public float Score { get; set; }
    public List<string> SemanticTags { get; set; } = new();
    public bool? IsSemanticMatch { get; set; }
}
