using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Schema retriever with fallback strategy:
/// 1. Try vector search (Qdrant or in-memory)
/// 2. Fallback to keyword-based search if vector search fails or returns 0 results
/// </summary>
public class SchemaRetriever
{
    private readonly IVectorStore _vectorStore;
    private readonly KeywordSchemaRetriever _keywordRetriever;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly RAGConfig _ragConfig;
    private readonly ILogger<SchemaRetriever> _logger;

    public SchemaRetriever(
        IVectorStore vectorStore,
        KeywordSchemaRetriever keywordRetriever,
        IEmbeddingClient embeddingClient,
        RAGConfig ragConfig,
        ILogger<SchemaRetriever> logger)
    {
        _vectorStore = vectorStore;
        _keywordRetriever = keywordRetriever;
        _embeddingClient = embeddingClient;
        _ragConfig = ragConfig;
        _logger = logger;
    }

    public async Task<RetrievedSchemaContext> RetrieveAsync(
        string question,
        DatabaseSchema fullSchema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SchemaRetriever] Retrieving schema for question...");

        // Try vector search first
        if (await _vectorStore.IsAvailableAsync(cancellationToken))
        {
            try
            {
                _logger.LogDebug("[SchemaRetriever] Attempting vector search...");

                // 1. Generate query embedding
                var queryEmbedding = await _embeddingClient.GenerateEmbeddingAsync(question, cancellationToken);

                // 2. Search vector DB
                var searchResults = await _vectorStore.SearchAsync(
                    queryVector: queryEmbedding,
                    limit: _ragConfig.TopK,
                    scoreThreshold: (float)_ragConfig.MinimumScore,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("[SchemaRetriever] Vector search found {Count} results", searchResults.Count);

                // 3. If we got results, use them
                if (searchResults.Count > 0)
                {
                    var context = BuildSchemaContext(searchResults, fullSchema);

                    _logger.LogInformation(
                        "[SchemaRetriever] ✓ Vector search: {Tables} tables, {Rels} relationships",
                        context.RelevantTables.Count,
                        context.RelevantRelationships.Count);

                    return context;
                }

                _logger.LogWarning("[SchemaRetriever] Vector search returned 0 results, falling back to keywords");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SchemaRetriever] Vector search failed, falling back to keywords");
            }
        }
        else
        {
            _logger.LogWarning("[SchemaRetriever] Vector store unavailable, using keyword fallback");
        }

        // Fallback to keyword-based search
        _logger.LogInformation("[SchemaRetriever] Using keyword-based retrieval");
        return _keywordRetriever.RetrieveByKeywords(question, fullSchema, _ragConfig.MaxContextTables);
    }

    private RetrievedSchemaContext BuildSchemaContext(
        List<VectorSearchResult> searchResults,
        DatabaseSchema fullSchema)
    {
        var context = new RetrievedSchemaContext();
        var tableNames = new HashSet<string>();
        var relationships = new List<RelationshipInfo>();

        // Process each search result
        foreach (var result in searchResults)
        {
            var payload = result.Payload;
            var type = GetPayloadString(payload, "type");
            var score = result.Score;

            // Add to matches
            var match = new SchemaMatch
            {
                Type = type,
                Score = score,
                Content = GetPayloadString(payload, "content")
            };

            if (type == "table")
            {
                var tableName = GetPayloadString(payload, "table_name");
                match.TableName = tableName;
                tableNames.Add(tableName);
            }
            else if (type == "column")
            {
                var tableName = GetPayloadString(payload, "table_name");
                var columnName = GetPayloadString(payload, "column_name");
                match.TableName = tableName;
                match.ColumnName = columnName;
                tableNames.Add(tableName);
            }
            else if (type == "relationship")
            {
                var fromTable = GetPayloadString(payload, "from_table");
                var toTable = GetPayloadString(payload, "to_table");
                tableNames.Add(fromTable);
                tableNames.Add(toTable);

                var rel = new RelationshipInfo
                {
                    FromTable = fromTable,
                    FromColumn = GetPayloadString(payload, "from_column"),
                    ToTable = toTable,
                    ToColumn = GetPayloadString(payload, "to_column")
                };
                relationships.Add(rel);
            }

            context.Matches.Add(match);
        }

        // Get full table info for relevant tables
        foreach (var tableName in tableNames.Take(_ragConfig.MaxContextTables))
        {
            var table = fullSchema.Tables.FirstOrDefault(t =>
                ExtractTableName(t.TableName).Equals(ExtractTableName(tableName), StringComparison.OrdinalIgnoreCase));

            if (table != null)
            {
                context.RelevantTables.Add(table);
                context.TableColumns[table.TableName] = table.Columns;
            }
        }

        // Add relationships between relevant tables
        foreach (var rel in relationships)
        {
            var fromTableName = ExtractTableName(rel.FromTable);
            var toTableName = ExtractTableName(rel.ToTable);

            if (context.RelevantTables.Any(t => ExtractTableName(t.TableName) == fromTableName) &&
                context.RelevantTables.Any(t => ExtractTableName(t.TableName) == toTableName))
            {
                context.RelevantRelationships.Add(rel);
            }
        }

        // Also add relationships from full schema that connect our tables
        foreach (var table in context.RelevantTables)
        {
            var tableRelationships = fullSchema.Relationships.Where(r =>
                ExtractTableName(r.FromTable).Equals(ExtractTableName(table.TableName), StringComparison.OrdinalIgnoreCase) ||
                ExtractTableName(r.ToTable).Equals(ExtractTableName(table.TableName), StringComparison.OrdinalIgnoreCase));

            foreach (var rel in tableRelationships)
            {
                if (!context.RelevantRelationships.Any(r =>
                    r.FromTable == rel.FromTable && r.FromColumn == rel.FromColumn &&
                    r.ToTable == rel.ToTable && r.ToColumn == rel.ToColumn))
                {
                    context.RelevantRelationships.Add(rel);
                }
            }
        }

        return context;
    }

    private static string GetPayloadString(Dictionary<string, object> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return value?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractTableName(string fullName)
    {
        // Handle "schema.table" format
        var parts = fullName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }
}