using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Schema retriever with hybrid search strategy:
/// 1. Vector similarity search (Qdrant or in-memory)
/// 2. Keyword matching on schema elements
/// 3. Schema graph traversal for related tables
/// 4. Combined weighted scoring and ranking
/// </summary>
public class SchemaRetriever
{
    private readonly IVectorStore _vectorStore;
    private readonly KeywordSchemaRetriever _keywordRetriever;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly RAGConfig _ragConfig;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache? _distributedCache; // ✅ PHASE-2 TASK-07: Use Redis for embedding cache
    private readonly ILogger<SchemaRetriever> _logger;

    // Cache configuration
    private const int CacheExpirationMinutes = 60;

    public SchemaRetriever(
        IVectorStore vectorStore,
        KeywordSchemaRetriever keywordRetriever,
        IEmbeddingClient embeddingClient,
        RAGConfig ragConfig,
        ILogger<SchemaRetriever> logger,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null) // ✅ PHASE-2 TASK-07: Inject IDistributedCache
    {
        _vectorStore = vectorStore;
        _keywordRetriever = keywordRetriever;
        _embeddingClient = embeddingClient;
        _ragConfig = ragConfig;
        _distributedCache = distributedCache; // ✅ PHASE-2 TASK-07: Store distributed cache
        _logger = logger;
    }

    public async Task<RetrievedSchemaContext> RetrieveAsync(
        string question,
        DatabaseSchema fullSchema,
        CancellationToken cancellationToken = default)
    {
        return await RetrieveAsync(question, fullSchema, null, cancellationToken);
    }

    public async Task<RetrievedSchemaContext> RetrieveAsync(
        string question,
        DatabaseSchema fullSchema,
        string? connectionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SchemaRetriever] Retrieving schema for question...");

        // 1. Get or generate query embedding (with caching)
        float[]? queryEmbedding = null;
        try
        {
            queryEmbedding = await GetOrGenerateQueryEmbeddingAsync(question, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SchemaRetriever] Failed to generate query embedding, will rely on keyword search only");
        }

        var vectorResults = new List<VectorSearchResult>();
        var keywordResults = new List<SchemaMatch>();
        var retrievalStrategies = new List<string>();
        var errors = new List<string>();

        // 2. Vector similarity search (only if we have an embedding)
        if (queryEmbedding != null && await _vectorStore.IsAvailableAsync(cancellationToken))
        {
            try
            {
                _logger.LogDebug("[SchemaRetriever] Attempting vector search...");

                // Create filter for connectionId if provided
                Dictionary<string, object>? filter = null;
                if (!string.IsNullOrEmpty(connectionId))
                {
                    filter = new Dictionary<string, object> { ["connection_id"] = connectionId };
                }

                vectorResults = await _vectorStore.SearchAsync(
                    queryVector: queryEmbedding,
                    limit: _ragConfig.TopK,
                    scoreThreshold: (float)_ragConfig.MinimumScore,
                    filter: filter,
                    cancellationToken: cancellationToken);

                if (vectorResults.Count > 0)
                {
                    retrievalStrategies.Add("vector");
                    _logger.LogDebug("[SchemaRetriever] Vector search: {Count} results", vectorResults.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaRetriever] Vector search failed, continuing with other strategies");
                errors.Add($"Vector search failed: {ex.Message}");
            }
        }

        // 3. Keyword matching
        if (_ragConfig.EnableHybridSearch)
        {
            try
            {
                _logger.LogDebug("[SchemaRetriever] Performing keyword search...");
                var keywordContext = _keywordRetriever.RetrieveByKeywords(
                    question, fullSchema, _ragConfig.MaxContextTables);
                keywordResults = keywordContext.Matches;
                if (keywordResults.Count == 0 && keywordContext.RelevantTables.Count > 0)
                {
                    keywordResults = keywordContext.RelevantTables
                        .Select(table => new SchemaMatch
                        {
                            Type = "table",
                            ElementType = "table",
                            TableName = table.TableName,
                            ElementName = table.TableName,
                            Score = 1,
                            Content = $"Table: {table.TableName}"
                        })
                        .ToList();
                }
                retrievalStrategies.Add("keyword");
                _logger.LogDebug("[SchemaRetriever] Keyword search: {Count} results", keywordResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaRetriever] Keyword search failed");
                errors.Add($"Keyword search failed: {ex.Message}");
            }
        }

        // 4. Check if all strategies failed
        if (vectorResults.Count == 0 && keywordResults.Count == 0)
        {
            _logger.LogError("[SchemaRetriever] All retrieval strategies failed or returned no results");

            // Return empty result with error message
            return new RetrievedSchemaContext
            {
                RetrievalStrategies = retrievalStrategies,
                ErrorMessage = errors.Count > 0
                    ? $"All retrieval strategies failed: {string.Join("; ", errors)}"
                    : "No relevant schema elements found for the query"
            };
        }

        // 5. Merge and deduplicate results
        var mergedResults = MergeResults(vectorResults, keywordResults);

        // 6. Graph traversal for related tables
        if (_ragConfig.EnableHybridSearch && mergedResults.Count > 0)
        {
            try
            {
                _logger.LogDebug("[SchemaRetriever] Performing graph traversal...");
                var expandedResults = TraverseSchemaGraph(mergedResults, fullSchema);
                mergedResults = expandedResults;
                retrievalStrategies.Add("graph");
                _logger.LogDebug("[SchemaRetriever] Graph traversal: {Count} total results", mergedResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaRetriever] Graph traversal failed, using results without graph expansion");
                errors.Add($"Graph traversal failed: {ex.Message}");
            }
        }

        // 7. Rank by combined score
        var rankedResults = RankByCombinedScore(mergedResults);

        // 8. Build final context
        var context = BuildSchemaContextFromScoredElements(rankedResults, fullSchema);
        context.RetrievalStrategies = retrievalStrategies;

        _logger.LogInformation(
            "[SchemaRetriever] Hybrid retrieval: {Tables} tables, {Rels} relationships, Strategies: {Strategies}",
            context.RelevantTables.Count,
            context.RelevantRelationships.Count,
            string.Join("+", retrievalStrategies));

        return context;
    }


    private async Task<float[]> GetOrGenerateQueryEmbeddingAsync(
        string query,
        CancellationToken cancellationToken)
    {
        // ✅ PHASE-2 TASK-07: Use Redis for distributed embedding cache
        // Hash the query to create a stable cache key
        var queryHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(query));
        var cacheKey = $"TextToSqlAgent:QueryEmbedding:{Convert.ToHexString(queryHash)}";

        // Check Redis cache first
        if (_distributedCache != null)
        {
            try
            {
                var cachedBytes = await _distributedCache.GetAsync(cacheKey, cancellationToken);
                if (cachedBytes != null && cachedBytes.Length > 0)
                {
                    // Deserialize float[] from bytes
                    var cachedEmbedding = DeserializeEmbedding(cachedBytes);
                    _logger.LogDebug(
                        "[SchemaRetriever] ✅ Using cached embedding from Redis (hash: {Hash})",
                        Convert.ToHexString(queryHash).Substring(0, 8));
                    return cachedEmbedding;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SchemaRetriever] Failed to read embedding from Redis cache");
            }
        }

        try
        {
            // Generate new embedding
            var embedding = await _embeddingClient.GenerateEmbeddingAsync(query, cancellationToken);

            // Cache in Redis with 1 hour expiration
            if (_distributedCache != null)
            {
                try
                {
                    var embeddingBytes = SerializeEmbedding(embedding);
                    var cacheOptions = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes)
                    };
                    await _distributedCache.SetAsync(cacheKey, embeddingBytes, cacheOptions, cancellationToken);

                    _logger.LogDebug(
                        "[SchemaRetriever] ✅ Generated and cached embedding in Redis (TTL: {Minutes}min, size: {Size} bytes)",
                        CacheExpirationMinutes, embeddingBytes.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SchemaRetriever] Failed to cache embedding in Redis (non-critical)");
                }
            }

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SchemaRetriever] Failed to generate query embedding");
            throw;
        }
    }

    /// <summary>
    /// Serialize float[] embedding to byte[] for Redis storage.
    /// Format: 4 bytes per float (little-endian).
    /// </summary>
    private static byte[] SerializeEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Deserialize byte[] from Redis back to float[] embedding.
    /// </summary>
    private static float[] DeserializeEmbedding(byte[] bytes)
    {
        var embedding = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
        return embedding;
    }

    /// <summary>
    /// Placeholder for TraverseSchemaGraph - to be implemented in task 11.5
    /// </summary>
    /// <summary>
    /// Traverses the schema graph to find related tables connected by foreign keys.
    /// Adds related tables with a graph score to expand the result set.
    /// </summary>
    private List<ScoredSchemaElement> TraverseSchemaGraph(
        List<ScoredSchemaElement> seedResults,
        DatabaseSchema fullSchema)
    {
        var expanded = new Dictionary<string, ScoredSchemaElement>();

        // Add seed results
        foreach (var result in seedResults)
        {
            var key = GetElementKey(result.Element);
            expanded[key] = result;
        }

        // Extract table names from seed results
        var seedTables = new HashSet<string>();
        foreach (var result in seedResults)
        {
            var tableName = GetTableNameFromElement(result.Element);
            if (!string.IsNullOrEmpty(tableName))
            {
                seedTables.Add(tableName);
            }
        }

        _logger.LogDebug("[SchemaRetriever] Graph traversal starting from {Count} seed tables", seedTables.Count);

        // Traverse relationships to find related tables
        foreach (var tableName in seedTables)
        {
            var relatedTables = fullSchema.Relationships
                .Where(r => r.FromTable == tableName || r.ToTable == tableName)
                .SelectMany(r => new[] { r.FromTable, r.ToTable })
                .Distinct()
                .Where(t => !seedTables.Contains(t));

            foreach (var relatedTable in relatedTables)
            {
                var table = fullSchema.Tables.FirstOrDefault(t => t.TableName == relatedTable);
                if (table != null)
                {
                    var key = $"table:{relatedTable}";
                    if (!expanded.ContainsKey(key))
                    {
                        // Create a SchemaMatch for the related table
                        var schemaMatch = new SchemaMatch
                        {
                            Type = "table",
                            ElementType = "table",
                            TableName = relatedTable,
                            ElementName = relatedTable,
                            Score = 0.5, // Graph score
                            Content = $"Table: {relatedTable}"
                        };

                        expanded[key] = new ScoredSchemaElement
                        {
                            Element = schemaMatch,
                            VectorScore = 0,
                            KeywordScore = 0,
                            GraphScore = 0.5f // Related table score
                        };

                        _logger.LogDebug("[SchemaRetriever] Added related table {Table} via graph traversal", relatedTable);
                    }
                }
            }
        }

        _logger.LogDebug("[SchemaRetriever] Graph traversal expanded from {Seed} to {Total} elements",
            seedResults.Count, expanded.Count);

        return expanded.Values.ToList();
    }

    /// <summary>
    /// Extracts the table name from a schema element.
    /// </summary>
    private string GetTableNameFromElement(object element)
    {
        return element switch
        {
            VectorSearchResult vsr when vsr.Payload.TryGetValue("table_name", out var tableNameObj)
                => tableNameObj?.ToString() ?? string.Empty,
            SchemaMatch sm => sm.TableName,
            TableInfo ti => ti.TableName,
            _ => string.Empty
        };
    }


    /// <summary>
    /// Ranks results by combined weighted score from all retrieval strategies.
    /// Uses configured weights: vector (0.5), keyword (0.3), graph (0.2).
    /// </summary>
    private List<ScoredSchemaElement> RankByCombinedScore(List<ScoredSchemaElement> results)
    {
        // Calculate combined score for each result using weighted formula
        foreach (var result in results)
        {
            result.CombinedScore =
                (result.VectorScore * _ragConfig.VectorWeight) +
                (result.KeywordScore * _ragConfig.KeywordWeight) +
                (result.GraphScore * _ragConfig.GraphWeight);
        }

        // Sort by combined score descending
        var ranked = results
            .OrderByDescending(r => r.CombinedScore)
            .ToList();

        _logger.LogDebug("[SchemaRetriever] Ranked {Count} results by combined score (V:{VW}, K:{KW}, G:{GW})",
            ranked.Count, _ragConfig.VectorWeight, _ragConfig.KeywordWeight, _ragConfig.GraphWeight);

        return ranked;
    }

    /// <summary>
    /// Builds a RetrievedSchemaContext from scored schema elements.
    /// Populates ElementScores dictionary with combined scores for each element.
    /// </summary>
    private RetrievedSchemaContext BuildSchemaContextFromScoredElements(
        List<ScoredSchemaElement> rankedResults,
        DatabaseSchema fullSchema)
    {
        var context = new RetrievedSchemaContext();
        var tableNames = new HashSet<string>();
        var elementScores = new Dictionary<string, float>();

        // Process each scored element
        foreach (var scoredElement in rankedResults)
        {
            var element = scoredElement.Element;
            var key = GetElementKey(element);

            // Store the combined score for this element
            elementScores[key] = scoredElement.CombinedScore;

            // Extract table names and build context based on element type
            switch (element)
            {
                case VectorSearchResult vsr:
                    ProcessVectorSearchResult(vsr, tableNames, context);
                    break;

                case SchemaMatch sm:
                    ProcessSchemaMatch(sm, tableNames, context);
                    break;

                case TableInfo ti:
                    tableNames.Add(ti.TableName);
                    break;
            }
        }

        // Add relevant tables from full schema
        foreach (var tableName in tableNames)
        {
            var table = fullSchema.Tables.FirstOrDefault(t => t.TableName == tableName);
            if (table != null && !context.RelevantTables.Any(t => t.TableName == tableName))
            {
                context.RelevantTables.Add(table);
                context.TableColumns[tableName] = table.Columns;
            }
        }

        // Add relevant relationships
        foreach (var relationship in fullSchema.Relationships)
        {
            if (tableNames.Contains(relationship.FromTable) || tableNames.Contains(relationship.ToTable))
            {
                if (!context.RelevantRelationships.Any(r =>
                    r.FromTable == relationship.FromTable &&
                    r.FromColumn == relationship.FromColumn &&
                    r.ToTable == relationship.ToTable &&
                    r.ToColumn == relationship.ToColumn))
                {
                    context.RelevantRelationships.Add(relationship);
                }
            }
        }

        // Set element scores dictionary
        context.ElementScores = elementScores;

        _logger.LogDebug("[SchemaRetriever] Built context with {Tables} tables, {Rels} relationships, {Scores} scored elements",
            context.RelevantTables.Count, context.RelevantRelationships.Count, elementScores.Count);

        return context;
    }

    /// <summary>
    /// Processes a VectorSearchResult and adds relevant information to the context.
    /// </summary>
    private void ProcessVectorSearchResult(VectorSearchResult vsr, HashSet<string> tableNames, RetrievedSchemaContext context)
    {
        if (vsr.Payload.TryGetValue("type", out var typeObj) && typeObj is string type)
        {
            if (type == "table" && vsr.Payload.TryGetValue("table_name", out var tableNameObj))
            {
                var tableName = tableNameObj?.ToString();
                if (!string.IsNullOrEmpty(tableName))
                {
                    tableNames.Add(tableName);
                }
            }
            else if (type == "column" &&
                     vsr.Payload.TryGetValue("table_name", out var tblNameObj))
            {
                var tableName = tblNameObj?.ToString();
                if (!string.IsNullOrEmpty(tableName))
                {
                    tableNames.Add(tableName);
                }
            }
            else if (type == "relationship" &&
                     vsr.Payload.TryGetValue("from_table", out var fromTableObj) &&
                     vsr.Payload.TryGetValue("to_table", out var toTableObj))
            {
                var fromTable = fromTableObj?.ToString();
                var toTable = toTableObj?.ToString();
                if (!string.IsNullOrEmpty(fromTable)) tableNames.Add(fromTable);
                if (!string.IsNullOrEmpty(toTable)) tableNames.Add(toTable);
            }
        }
    }

    /// <summary>
    /// Processes a SchemaMatch and adds relevant information to the context.
    /// </summary>
    private void ProcessSchemaMatch(SchemaMatch sm, HashSet<string> tableNames, RetrievedSchemaContext context)
    {
        if (!string.IsNullOrEmpty(sm.TableName))
        {
            tableNames.Add(sm.TableName);
        }

        // Add to matches list for backward compatibility
        if (!context.Matches.Any(m => m.ElementName == sm.ElementName && m.TableName == sm.TableName))
        {
            context.Matches.Add(sm);
            context.SchemaMatches.Add(sm);
        }
    }

    /// <summary>
    /// Merges vector and keyword search results, deduplicating overlapping elements
    /// and preserving the highest score for each unique element.
    /// </summary>
    private List<ScoredSchemaElement> MergeResults(
        List<VectorSearchResult> vectorResults,
        List<SchemaMatch> keywordResults)
    {
        var merged = new Dictionary<string, ScoredSchemaElement>();

        // Add vector results
        foreach (var result in vectorResults)
        {
            var key = GetElementKey(result);
            merged[key] = new ScoredSchemaElement
            {
                Element = result,
                VectorScore = result.Score,
                KeywordScore = 0,
                GraphScore = 0
            };
        }

        // Merge keyword results
        foreach (var result in keywordResults)
        {
            var key = GetElementKey(result);
            if (merged.ContainsKey(key))
            {
                // Element already exists from vector search - preserve higher score
                merged[key].KeywordScore = (float)result.Score;
            }
            else
            {
                // New element from keyword search
                merged[key] = new ScoredSchemaElement
                {
                    Element = result,
                    VectorScore = 0,
                    KeywordScore = (float)result.Score,
                    GraphScore = 0
                };
            }
        }

        _logger.LogDebug("[SchemaRetriever] Merged {Total} unique elements from {Vector} vector + {Keyword} keyword results",
            merged.Count, vectorResults.Count, keywordResults.Count);

        return merged.Values.ToList();
    }

    /// <summary>
    /// Generates a unique key for a schema element to enable deduplication.
    /// </summary>
    private string GetElementKey(object element)
    {
        return element switch
        {
            VectorSearchResult vsr => GetKeyFromPayload(vsr.Payload),
            SchemaMatch sm => GetKeyFromSchemaMatch(sm),
            _ => element.GetHashCode().ToString()
        };
    }

    /// <summary>
    /// Extracts a unique key from VectorSearchResult payload.
    /// </summary>
    private string GetKeyFromPayload(Dictionary<string, object> payload)
    {
        if (payload.TryGetValue("type", out var typeObj) && typeObj is string type)
        {
            if (type == "table" && payload.TryGetValue("table_name", out var tableNameObj))
            {
                return $"table:{tableNameObj}";
            }
            else if (type == "column" &&
                     payload.TryGetValue("table_name", out var tblNameObj) &&
                     payload.TryGetValue("column_name", out var colNameObj))
            {
                return $"column:{tblNameObj}.{colNameObj}";
            }
            else if (type == "relationship" &&
                     payload.TryGetValue("from_table", out var fromTableObj) &&
                     payload.TryGetValue("to_table", out var toTableObj))
            {
                return $"relationship:{fromTableObj}->{toTableObj}";
            }
        }

        // Fallback to ID if available
        if (payload.TryGetValue("id", out var idObj))
        {
            return $"unknown:{idObj}";
        }

        return $"unknown:{payload.GetHashCode()}";
    }

    /// <summary>
    /// Extracts a unique key from SchemaMatch.
    /// </summary>
    private string GetKeyFromSchemaMatch(SchemaMatch match)
    {
        var type = !string.IsNullOrEmpty(match.ElementType) ? match.ElementType : match.Type;

        if (type == "table")
        {
            return $"table:{match.TableName}";
        }
        else if (type == "column" && !string.IsNullOrEmpty(match.ColumnName))
        {
            return $"column:{match.TableName}.{match.ColumnName}";
        }
        else if (type == "relationship")
        {
            return $"relationship:{match.ElementName}";
        }

        return $"{type}:{match.ElementName}";
    }
}
