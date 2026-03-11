using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Advanced schema linking with entity recognition, hybrid search, and relationship inference
/// </summary>
public class AdvancedSchemaLinker
{
    private readonly EntityRecognizer _entityRecognizer;
    private readonly HybridSearchEngine _hybridSearch;
    private readonly RelationshipInference _relationshipInference;
    private readonly ILogger<AdvancedSchemaLinker> _logger;

    public AdvancedSchemaLinker(
        EntityRecognizer entityRecognizer,
        HybridSearchEngine hybridSearch,
        RelationshipInference relationshipInference,
        ILogger<AdvancedSchemaLinker> logger)
    {
        _entityRecognizer = entityRecognizer;
        _hybridSearch = hybridSearch;
        _relationshipInference = relationshipInference;
        _logger = logger;
    }

    public async Task<RetrievedSchemaContext> LinkSchemaAsync(
        string question,
        DatabaseSchema fullSchema,
        int topK = 5,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[AdvancedSchemaLinker] Linking schema for: {Question}", question);

        // Step 1: Entity Recognition
        var entities = await _entityRecognizer.RecognizeAsync(question, ct);
        _logger.LogDebug("[AdvancedSchemaLinker] Recognized {Count} entities", entities.AllEntities.Count);

        // Step 2: Hybrid Search (Semantic + Keyword)
        var scoredElements = await _hybridSearch.SearchAsync(question, entities, fullSchema, topK * 2, ct);
        _logger.LogDebug("[AdvancedSchemaLinker] Hybrid search found {Count} candidates", scoredElements.Count);

        // Step 3: Select top tables and columns
        var selectedTables = scoredElements
            .Where(e => e.ElementType == "table")
            .OrderByDescending(e => e.HybridScore)
            .Take(topK)
            .Select(e => e.ElementName)
            .Distinct()
            .ToList();

        var selectedColumns = scoredElements
            .Where(e => e.ElementType == "column")
            .OrderByDescending(e => e.HybridScore)
            .Take(topK * 2)
            .ToList();

        _logger.LogInformation("[AdvancedSchemaLinker] Selected {Tables} tables, {Columns} columns",
            selectedTables.Count, selectedColumns.Count);

        // Step 4: Find JOIN paths between tables
        var relationships = _relationshipInference.FindJoinPath(selectedTables, fullSchema);
        _logger.LogDebug("[AdvancedSchemaLinker] Found {Count} relationships", relationships.Count);

        // Step 5: Add intermediate tables if needed
        var allTables = _relationshipInference.AddIntermediateTables(
            selectedTables, relationships, fullSchema);

        // Step 6: Build context
        var context = new RetrievedSchemaContext
        {
            RelevantTables = allTables,
            RelevantRelationships = relationships
        };

        // Add columns for each table
        foreach (var table in allTables)
        {
            // Add all columns from selected tables
            if (selectedTables.Contains(table.TableName, StringComparer.OrdinalIgnoreCase))
            {
                context.TableColumns[table.TableName] = table.Columns;
            }
            else
            {
                // For intermediate tables, only add key columns
                var keyColumns = table.Columns
                    .Where(c => c.IsPrimaryKey || c.IsForeignKey)
                    .ToList();
                context.TableColumns[table.TableName] = keyColumns;
            }
        }

        // Add schema matches for tracking
        foreach (var element in scoredElements.Take(topK))
        {
            context.SchemaMatches.Add(new SchemaMatch
            {
                ElementType = element.ElementType,
                ElementName = element.ElementName,
                TableName = element.TableName ?? "",
                Score = element.HybridScore
            });
        }

        _logger.LogInformation("[AdvancedSchemaLinker] Linked schema: {Tables} tables, {Rels} relationships",
            context.RelevantTables.Count, context.RelevantRelationships.Count);

        return context;
    }

    /// <summary>
    /// Map entities to actual schema elements
    /// </summary>
    public void MapEntitiesToSchema(
        EntityRecognitionResult entities,
        DatabaseSchema schema)
    {
        // Map table entities
        foreach (var entity in entities.Tables)
        {
            var matchedTable = schema.Tables
                .FirstOrDefault(t => t.TableName.Equals(entity.Text, StringComparison.OrdinalIgnoreCase));

            if (matchedTable != null)
            {
                entity.MappedTo = matchedTable.TableName;
                entity.Confidence = 1.0;
            }
            else
            {
                // Try fuzzy match
                var fuzzyMatch = schema.Tables
                    .Select(t => new { Table = t, Score = CalculateSimilarity(entity.Text, t.TableName) })
                    .Where(x => x.Score > 0.7)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (fuzzyMatch != null)
                {
                    entity.MappedTo = fuzzyMatch.Table.TableName;
                    entity.Confidence = fuzzyMatch.Score;
                }
            }
        }

        // Map column entities
        foreach (var entity in entities.Columns)
        {
            foreach (var table in schema.Tables)
            {
                var matchedColumn = table.Columns
                    .FirstOrDefault(c => c.ColumnName.Equals(entity.Text, StringComparison.OrdinalIgnoreCase));

                if (matchedColumn != null)
                {
                    entity.MappedTo = $"{table.TableName}.{matchedColumn.ColumnName}";
                    entity.Confidence = 1.0;
                    break;
                }
            }
        }
    }

    private double CalculateSimilarity(string s1, string s2)
    {
        s1 = s1.ToLower();
        s2 = s2.ToLower();

        if (s1 == s2) return 1.0;
        if (s1.Contains(s2) || s2.Contains(s1)) return 0.8;

        // Simple character overlap
        var common = s1.Intersect(s2).Count();
        var total = Math.Max(s1.Length, s2.Length);
        return (double)common / total;
    }
}
