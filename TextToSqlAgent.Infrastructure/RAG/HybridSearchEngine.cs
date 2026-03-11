using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Hybrid search combining semantic (embedding) and keyword (exact match) search
/// </summary>
public class HybridSearchEngine
{
    private readonly IEmbeddingClient _embedder;
    private readonly QdrantService _qdrant;
    private readonly ILogger<HybridSearchEngine> _logger;

    // Weights for hybrid scoring
    private const double SemanticWeight = 0.6;
    private const double KeywordWeight = 0.4;

    public HybridSearchEngine(
        IEmbeddingClient embedder,
        QdrantService qdrant,
        ILogger<HybridSearchEngine> logger)
    {
        _embedder = embedder;
        _qdrant = qdrant;
        _logger = logger;
    }

    public async Task<List<ScoredSchemaElement>> SearchAsync(
        string query,
        EntityRecognitionResult entities,
        DatabaseSchema fullSchema,
        int topK = 20,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[HybridSearch] Searching for: {Query}", query);

        // 1. Semantic Search (Embedding-based)
        var semanticResults = await SemanticSearchAsync(query, topK, ct);

        // 2. Keyword Search (Exact + Fuzzy matching)
        var keywordResults = KeywordSearch(entities, fullSchema);

        // 3. Merge and score
        var mergedResults = MergeResults(semanticResults, keywordResults);

        // 4. Sort by hybrid score
        var sortedResults = mergedResults
            .OrderByDescending(r => r.HybridScore)
            .Take(topK)
            .ToList();

        _logger.LogInformation("[HybridSearch] Found {Count} results (semantic: {Semantic}, keyword: {Keyword})",
            sortedResults.Count, semanticResults.Count, keywordResults.Count);

        return sortedResults;
    }

    private async Task<List<ScoredSchemaElement>> SemanticSearchAsync(
        string query,
        int topK,
        CancellationToken ct)
    {
        try
        {
            // Generate embedding
            var embedding = await _embedder.GenerateEmbeddingAsync(query, ct);

            // Search in Qdrant
            var results = await _qdrant.SearchAsync(embedding, (ulong)topK, 0.7, ct);

            return results.Select(r => new ScoredSchemaElement
            {
                ElementType = r.Payload.ContainsKey("type") ? r.Payload["type"].ToString() ?? "" : "",
                ElementName = r.Payload.ContainsKey("name") ? r.Payload["name"].ToString() ?? "" : "",
                TableName = r.Payload.ContainsKey("table") ? r.Payload["table"].ToString() : null,
                SemanticScore = r.Score,
                KeywordScore = 0,
                HybridScore = r.Score * SemanticWeight
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HybridSearch] Semantic search failed");
            return new List<ScoredSchemaElement>();
        }
    }

    private List<ScoredSchemaElement> KeywordSearch(
        EntityRecognitionResult entities,
        DatabaseSchema fullSchema)
    {
        var results = new List<ScoredSchemaElement>();

        // Search for table matches
        foreach (var entity in entities.Tables)
        {
            foreach (var table in fullSchema.Tables)
            {
                var score = CalculateKeywordScore(entity.Text, table.TableName);
                if (score > 0.5)
                {
                    results.Add(new ScoredSchemaElement
                    {
                        ElementType = "table",
                        ElementName = table.TableName,
                        TableName = table.TableName,
                        SemanticScore = 0,
                        KeywordScore = score,
                        HybridScore = score * KeywordWeight
                    });
                }
            }
        }

        // Search for column matches
        foreach (var entity in entities.Columns)
        {
            foreach (var table in fullSchema.Tables)
            {
                foreach (var column in table.Columns)
                {
                    var score = CalculateKeywordScore(entity.Text, column.ColumnName);
                    if (score > 0.5)
                    {
                        results.Add(new ScoredSchemaElement
                        {
                            ElementType = "column",
                            ElementName = column.ColumnName,
                            TableName = table.TableName,
                            SemanticScore = 0,
                            KeywordScore = score,
                            HybridScore = score * KeywordWeight
                        });
                    }
                }
            }
        }

        return results;
    }

    private double CalculateKeywordScore(string query, string target)
    {
        query = query.ToLower();
        target = target.ToLower();

        // Exact match
        if (query == target)
            return 1.0;

        // Contains match
        if (target.Contains(query) || query.Contains(target))
            return 0.8;

        // Fuzzy match (Levenshtein distance)
        var distance = LevenshteinDistance(query, target);
        var maxLen = Math.Max(query.Length, target.Length);
        var similarity = 1.0 - (double)distance / maxLen;

        return similarity > 0.7 ? similarity : 0;
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        var d = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            d[0, j] = j;

        for (int j = 1; j <= s2.Length; j++)
        {
            for (int i = 1; i <= s1.Length; i++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(
                    d[i - 1, j] + 1,      // deletion
                    d[i, j - 1] + 1),     // insertion
                    d[i - 1, j - 1] + cost); // substitution
            }
        }

        return d[s1.Length, s2.Length];
    }

    private List<ScoredSchemaElement> MergeResults(
        List<ScoredSchemaElement> semanticResults,
        List<ScoredSchemaElement> keywordResults)
    {
        var merged = new Dictionary<string, ScoredSchemaElement>();

        // Add semantic results
        foreach (var result in semanticResults)
        {
            var key = $"{result.ElementType}:{result.TableName}:{result.ElementName}";
            merged[key] = result;
        }

        // Merge keyword results
        foreach (var result in keywordResults)
        {
            var key = $"{result.ElementType}:{result.TableName}:{result.ElementName}";

            if (merged.ContainsKey(key))
            {
                // Combine scores
                var existing = merged[key];
                existing.KeywordScore = result.KeywordScore;
                existing.HybridScore = (existing.SemanticScore * SemanticWeight) +
                                      (result.KeywordScore * KeywordWeight);
            }
            else
            {
                merged[key] = result;
            }
        }

        return merged.Values.ToList();
    }
}

/// <summary>
/// Schema element with hybrid scoring
/// </summary>
public class ScoredSchemaElement
{
    public string ElementType { get; set; } = ""; // table, column, relationship
    public string ElementName { get; set; } = "";
    public string? TableName { get; set; }
    public double SemanticScore { get; set; }
    public double KeywordScore { get; set; }
    public double HybridScore { get; set; }
}
