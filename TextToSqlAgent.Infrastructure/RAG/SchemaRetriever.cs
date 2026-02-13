using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Infrastructure.RAG;

public class SchemaRetriever
{
    private readonly QdrantService _qdrant;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly RAGConfig _ragConfig;

    private readonly ILogger<SchemaRetriever> _logger;

    public SchemaRetriever(
        QdrantService qdrant,
        IEmbeddingClient embeddingClient,
        RAGConfig ragConfig,
        ILogger<SchemaRetriever> logger)
    {
        _qdrant = qdrant;
        _embeddingClient = embeddingClient;
        _ragConfig = ragConfig;
        _logger = logger;
    }

    public async Task<RetrievedSchemaContext> RetrieveAsync(
        string question,
        DatabaseSchema fullSchema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Schema Retriever] Retrieving schema for question...");

        // 1. Generate query embedding
        var queryEmbedding = await _embeddingClient.GenerateEmbeddingAsync(question, cancellationToken);

        // 2. Search vector DB
        var searchResults = await _qdrant.SearchAsync(
            queryVector: queryEmbedding,
            limit: (ulong)_ragConfig.TopK,
            scoreThreshold: _ragConfig.MinimumScore,
            cancellationToken: cancellationToken);

        _logger.LogDebug("[Schema Retriever] Found {Count} relevant schema elements", searchResults.Count);

        // 3. Build context from results
        var context = BuildSchemaContext(searchResults, fullSchema);

        _logger.LogDebug(
            "[Schema Retriever] Context: {Tables} tables, {Relationships} relationships",
            context.RelevantTables.Count,
            context.RelevantRelationships.Count);

        return context;
    }

    private RetrievedSchemaContext BuildSchemaContext(
        List<Qdrant.Client.Grpc.ScoredPoint> searchResults,
        DatabaseSchema fullSchema)
    {
        var context = new RetrievedSchemaContext();
        var tableNames = new HashSet<string>();
        var relationships = new List<RelationshipInfo>();

        // Process each search result
        foreach (var result in searchResults)
        {
            var payload = result.Payload;
            var type = payload["type"].StringValue;
            var score = result.Score;

            // Add to matches
            var match = new SchemaMatch
            {
                Type = type,
                Score = score,
                Content = payload["content"].StringValue
            };

            if (type == "table")
            {
                var tableName = payload["table_name"].StringValue;
                match.TableName = tableName;
                tableNames.Add(tableName);
            }
            else if (type == "column")
            {
                var tableName = payload["table_name"].StringValue;
                var columnName = payload["column_name"].StringValue;
                match.TableName = tableName;
                match.ColumnName = columnName;
                tableNames.Add(tableName);
            }
            else if (type == "relationship")
            {
                var fromTable = payload["from_table"].StringValue;
                var toTable = payload["to_table"].StringValue;
                tableNames.Add(fromTable);
                tableNames.Add(toTable);

                var rel = new RelationshipInfo
                {
                    FromTable = fromTable,
                    FromColumn = payload["from_column"].StringValue,
                    ToTable = toTable,
                    ToColumn = payload["to_column"].StringValue
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

    private string ExtractTableName(string fullName)
    {
        // Handle "schema.table" format
        var parts = fullName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }
}