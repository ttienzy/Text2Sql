using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Keyword-based schema retrieval as fallback when vector search is unavailable
/// Uses simple text matching and heuristics
/// </summary>
public class KeywordSchemaRetriever
{
    private readonly ILogger<KeywordSchemaRetriever> _logger;

    public KeywordSchemaRetriever(ILogger<KeywordSchemaRetriever> logger)
    {
        _logger = logger;
    }

    public RetrievedSchemaContext RetrieveByKeywords(
        string question,
        DatabaseSchema fullSchema,
        int maxTables = 5)
    {
        _logger.LogInformation("[KeywordRetriever] Using keyword-based retrieval");

        var context = new RetrievedSchemaContext();
        var keywords = ExtractKeywords(question);

        _logger.LogDebug("[KeywordRetriever] Extracted keywords: {Keywords}",
            string.Join(", ", keywords));

        // Score each table based on keyword matches
        var tableScores = new Dictionary<TableInfo, int>();

        foreach (var table in fullSchema.Tables)
        {
            var score = 0;

            // Match table name
            foreach (var keyword in keywords)
            {
                if (table.TableName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10; // High weight for table name match
                }

                // Match column names
                foreach (var column in table.Columns)
                {
                    if (column.ColumnName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 5; // Medium weight for column name match
                    }
                }
            }

            if (score > 0)
            {
                tableScores[table] = score;
            }
        }

        // Select top N tables by score
        var relevantTables = tableScores
            .OrderByDescending(kvp => kvp.Value)
            .Take(maxTables)
            .Select(kvp => kvp.Key)
            .ToList();

        if (relevantTables.Count == 0)
        {
            _logger.LogWarning("[KeywordRetriever] No tables matched keywords, using heuristics");
            relevantTables = ApplyHeuristics(question, fullSchema, maxTables);
        }

        // Build context
        foreach (var table in relevantTables)
        {
            context.RelevantTables.Add(table);
            context.TableColumns[table.TableName] = table.Columns;

            var score = tableScores.TryGetValue(table, out var tableScore)
                ? tableScore
                : 1;

            var match = new SchemaMatch
            {
                Type = "table",
                ElementType = "table",
                TableName = table.TableName,
                ElementName = table.TableName,
                Score = score,
                Content = $"Table: {table.TableName}"
            };

            context.Matches.Add(match);
            context.SchemaMatches.Add(match);
            context.ElementScores[$"table:{table.TableName}"] = score;
        }

        // Add relationships between relevant tables
        var relevantTableNames = relevantTables.Select(t => t.TableName).ToHashSet();

        foreach (var rel in fullSchema.Relationships)
        {
            var fromTable = ExtractTableName(rel.FromTable);
            var toTable = ExtractTableName(rel.ToTable);

            if (relevantTableNames.Contains(fromTable) && relevantTableNames.Contains(toTable))
            {
                context.RelevantRelationships.Add(rel);

                var relationshipKey = $"{rel.FromTable}.{rel.FromColumn}->{rel.ToTable}.{rel.ToColumn}";
                if (!context.Matches.Any(m => m.ElementType == "relationship" && m.ElementName == relationshipKey))
                {
                    var match = new SchemaMatch
                    {
                        Type = "relationship",
                        ElementType = "relationship",
                        TableName = rel.FromTable,
                        ElementName = relationshipKey,
                        Score = 1,
                        Content = relationshipKey
                    };

                    context.Matches.Add(match);
                    context.SchemaMatches.Add(match);
                    context.ElementScores[$"relationship:{relationshipKey}"] = 1;
                }
            }
        }

        _logger.LogInformation(
            "[KeywordRetriever] Found {Tables} tables, {Rels} relationships",
            context.RelevantTables.Count,
            context.RelevantRelationships.Count);

        return context;
    }

    private List<string> ExtractKeywords(string question)
    {
        // Remove common words and extract meaningful keywords
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "up", "about", "into", "through", "during",
            "show", "me", "get", "find", "list", "all", "what", "how", "many",
            "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did",
            // Vietnamese stop words
            "các", "của", "và", "có", "là", "trong", "cho", "với", "từ",
            "tất", "cả", "những", "này", "đó", "thì", "được"
        };

        var words = question
            .ToLower()
            .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '-', '_' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToList();

        return words;
    }

    private List<TableInfo> ApplyHeuristics(
        string question,
        DatabaseSchema fullSchema,
        int maxTables)
    {
        var lowerQuestion = question.ToLower();

        // Heuristic 1: Common query patterns
        if (lowerQuestion.Contains("customer") || lowerQuestion.Contains("khách hàng"))
        {
            var customerTable = fullSchema.Tables.FirstOrDefault(t =>
                t.TableName.Contains("customer", StringComparison.OrdinalIgnoreCase));
            if (customerTable != null)
                return new List<TableInfo> { customerTable };
        }

        if (lowerQuestion.Contains("order") || lowerQuestion.Contains("đơn hàng"))
        {
            var orderTable = fullSchema.Tables.FirstOrDefault(t =>
                t.TableName.Contains("order", StringComparison.OrdinalIgnoreCase));
            if (orderTable != null)
                return new List<TableInfo> { orderTable };
        }

        if (lowerQuestion.Contains("product") || lowerQuestion.Contains("sản phẩm"))
        {
            var productTable = fullSchema.Tables.FirstOrDefault(t =>
                t.TableName.Contains("product", StringComparison.OrdinalIgnoreCase));
            if (productTable != null)
                return new List<TableInfo> { productTable };
        }

        // Heuristic 2: Return most commonly used tables (those with most relationships)
        var tableRelationshipCounts = fullSchema.Tables
            .Select(t => new
            {
                Table = t,
                RelationshipCount = fullSchema.Relationships.Count(r =>
                    r.FromTable.Contains(t.TableName, StringComparison.OrdinalIgnoreCase) ||
                    r.ToTable.Contains(t.TableName, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => x.RelationshipCount)
            .Take(maxTables)
            .Select(x => x.Table)
            .ToList();

        _logger.LogDebug("[KeywordRetriever] Using heuristic: tables with most relationships");
        return tableRelationshipCounts;
    }

    private static string ExtractTableName(string fullName)
    {
        // Handle "schema.table" format
        var parts = fullName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }
}
