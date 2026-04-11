using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed; // ✅ PHASE-1 TASK-03: Add for IDistributedCache extension methods
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.VectorDB;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage 3: Schema Retrieval — loads the DB schema, sets up Qdrant collection,
/// indexes schema into vector DB, then performs RAG to find relevant tables.
/// </summary>
public class SchemaRetrievalStage : IPipelineStage
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly DatabaseConfig _dbConfig;
    private readonly ISchemaCache? _schemaCache;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache? _distributedCache; // ✅ PHASE-1 TASK-03: Use Redis instead of IMemoryCache
    private readonly ILogger<SchemaRetrievalStage> _logger;

    // Mutable state — mirrors the original orchestrator caching behavior
    private DatabaseSchema? _cachedSchema;
    private bool _schemaIndexed;

    public string StageName => "Schema Retrieval";
    public AgentStage Stage => AgentStage.SCHEMA_RETRIEVAL;
    public double ProgressStart => 0.20;

    public SchemaRetrievalStage(
        IAgentServiceFactory serviceFactory,
        DatabaseConfig dbConfig,
        ILogger<SchemaRetrievalStage> logger,
        ISchemaCache? schemaCache = null,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null) // ✅ PHASE-1 TASK-03: Inject IDistributedCache
    {
        _serviceFactory = serviceFactory;
        _dbConfig = dbConfig;
        _logger = logger;
        _schemaCache = schemaCache;
        _distributedCache = distributedCache; // ✅ PHASE-1 TASK-03: Store distributed cache
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // ── Step 1: Load / scan database schema ──
        context.Steps.Add("Load database schema");

        if (context.Schema != null)
        {
            _cachedSchema = context.Schema;
            context.Steps.Add("Use context schema (skip scan)");

            // Still need to trigger indexing check quietly if it wasn't done
            await TryEnsureSchemaIndexedAsync(_cachedSchema, context.ConversationId, ct);
        }
        else
        {
            await EnsureSchemaLoadedAsync(context.Steps, ct);
            context.Schema = _cachedSchema;
        }

        context.TableNames = _cachedSchema?.Tables.Select(t => t.TableName).ToList() ?? new List<string>();

        // ── Step 2: RAG — retrieve relevant schema ──
        context.Steps.Add("RAG - Retrieve relevant schema");

        context.ReportProgress(AgentStage.SCHEMA_RETRIEVAL,
            "Finding relevant tables and relationships...", 0.35,
            "Using vector search to identify relevant schema");

        var schemaRetriever = _serviceFactory.GetSchemaRetriever();
        var queryText = context.NormalizedPrompt?.NormalizedText ?? context.EnrichedQuestion;

        var relevantSchema = await schemaRetriever.RetrieveAsync(queryText, _cachedSchema!, ct);

        _logger.LogDebug(
            "[SchemaRetrieval] RAG found: {Tables} tables, {Rels} relationships",
            relevantSchema.RelevantTables.Count, relevantSchema.RelevantRelationships.Count);

        // ── Step 3: Intent analysis (needed for SQL generation) ──
        // ✅ T6: Skip IntentAnalysis LLM for simple queries
        IntentAnalysis intent;

        if (relevantSchema.RelevantTables.Count == 0)
        {
            _logger.LogWarning("[SchemaRetrieval] RAG found 0 results, using fallback");

            var intentAnalyzer = _serviceFactory.GetIntentAnalyzer();
            intent = await intentAnalyzer.AnalyzeIntentAsync(queryText, context.TableNames, ct);
            relevantSchema = BuildFallbackSchema(intent.Target, _cachedSchema!);
        }
        else if (TryBuildSimpleIntent(queryText, context.IntentClassification, relevantSchema, out var simpleIntent))
        {
            // ✅ T6: Deterministic path — no LLM call needed
            intent = simpleIntent!;
            context.Steps.Add("Intent resolved (rule-based, skip LLM)");
            _logger.LogInformation(
                "[SchemaRetrieval] ⚡ Simple intent resolved locally: {Intent} → {Target} (skip LLM)",
                intent.Intent, intent.Target);
        }
        else
        {
            // Complex query — need full LLM intent analysis
            context.Steps.Add("Analyze intent (LLM)");
            var intentAnalyzer = _serviceFactory.GetIntentAnalyzer();
            intent = await intentAnalyzer.AnalyzeIntentAsync(
                queryText,
                relevantSchema.RelevantTables.Select(t => t.TableName).ToList(),
                ct);
        }

        context.Intent = intent;
        context.SchemaContext = relevantSchema;

        // Handle clarification from intent analysis
        if (intent.NeedsClarification)
        {
            var clarification = intent.ClarificationQuestion ?? "Question is unclear.";
            context.Response.Success = false;
            context.Response.Answer = clarification;
            context.Response.ErrorMessage = clarification;

            if (context.ConversationCtx != null)
            {
                var cm = _serviceFactory.GetConversationManager();
                cm.AddTurn(context.ConversationCtx, context.UserQuestion, clarification,
                    intent: intent.Intent, targetTable: intent.Target, success: false);
            }

            return StageResult.Stop("Clarification needed after intent analysis");
        }

        return StageResult.Continue();
    }

    #region T6: Simple Intent Detection (Rule-based, skip LLM)

    /// <summary>
    /// T6: Try to build IntentAnalysis deterministically for simple queries.
    /// Returns true if the query is simple enough to skip the LLM call.
    /// Decision is based on:
    ///   1. Primary: IntentType detection via regex (COUNT, LIST, DETAIL keywords)
    ///   2. Secondary: IntentClassification.ComplexityScore from classifier (< 0.5 = simple)
    /// </summary>
    private bool TryBuildSimpleIntent(
        string queryText,
        IntentClassificationResult? classification,
        RetrievedSchemaContext relevantSchema,
        out IntentAnalysis? intent)
    {
        intent = null;

        // Detect simple intent type from query text
        var detectedIntent = DetectSimpleQueryIntent(queryText);
        if (detectedIntent == null)
        {
            // Check complexity score as fallback signal
            if (classification?.ComplexityScore is > 0 and < 0.5)
            {
                detectedIntent = QueryIntent.LIST; // Default simple intent
            }
            else
            {
                return false; // Not a simple query
            }
        }

        // Resolve target table from IntentClassification entities or RAG results
        var targetTable = classification?.DetectedEntities?.FirstOrDefault()
            ?? relevantSchema.RelevantTables.FirstOrDefault()?.TableName
            ?? string.Empty;

        if (string.IsNullOrEmpty(targetTable))
        {
            return false; // Can't determine target without LLM
        }

        intent = new IntentAnalysis
        {
            Intent = detectedIntent.Value,
            Target = targetTable,
            Complexity = "Simple",
            RelatedEntities = relevantSchema.RelevantTables.Select(t => t.TableName).ToList(),
            NeedsClarification = false
        };

        return true;
    }

    /// <summary>
    /// Detect simple query intent type from text using regex patterns.
    /// Returns null if the query is ambiguous or complex.
    /// </summary>
    private static QueryIntent? DetectSimpleQueryIntent(string queryText)
    {
        var lower = queryText.ToLowerInvariant();

        // COUNT patterns (EN + VI)
        if (System.Text.RegularExpressions.Regex.IsMatch(lower,
            @"\b(?:count|how\s+many|có\s+bao\s+nhiêu|đếm|tổng\s+số)\b"))
        {
            return QueryIntent.COUNT;
        }

        // LIST patterns (EN + VI) — "show all", "list", "danh sách", "liệt kê"
        if (System.Text.RegularExpressions.Regex.IsMatch(lower,
            @"\b(?:show\s+all|list\s+all|list\b|display\s+all|danh\s+sách|liệt\s+kê|hiển\s+thị\s+tất\s+cả|xem\s+tất\s+cả)\b"))
        {
            return QueryIntent.LIST;
        }

        // DETAIL patterns (EN + VI) — "show me details", "chi tiết", "thông tin về"
        if (System.Text.RegularExpressions.Regex.IsMatch(lower,
            @"\b(?:detail|details\s+of|info\s+about|chi\s+tiết|thông\s+tin\s+về)\b"))
        {
            return QueryIntent.DETAIL;
        }

        // Simple SELECT keyword without complex clauses
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^\s*select\s+\*?\s+from\s+\w+\s*;?\s*$"))
        {
            return QueryIntent.LIST;
        }

        return null; // Ambiguous — needs LLM
    }

    #endregion

    #region Private helpers (extracted from EnhancedAgentOrchestrator)

    private async Task EnsureSchemaLoadedAsync(List<string> steps, CancellationToken ct)
    {
        if (_cachedSchema != null)
        {
            steps.Add("Use cached schema");
            return;
        }

        steps.Add("Scan database schema");

        var schemaScanner = _serviceFactory.GetSchemaScanner();
        _cachedSchema = await schemaScanner.ScanAsync(ct);

        // Set correct collection name before indexing
        try
        {
            var dbName = ExtractDatabaseName(_dbConfig);
            if (!string.IsNullOrEmpty(dbName))
            {
                var qdrantService = _serviceFactory.GetQdrantService();
                qdrantService.SetCollectionName(dbName);
                _logger.LogInformation("[SchemaRetrieval] Collection name set to: {Name}", dbName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SchemaRetrieval] Cannot set collection name, using default");
        }

        // Auto-index schema
        if (!_schemaIndexed)
        {
            steps.Add("Index schema into vector database");
            await TryEnsureSchemaIndexedAsync(_cachedSchema, ct: ct);
            _schemaIndexed = true;
        }
    }

    private async Task TryEnsureSchemaIndexedAsync(DatabaseSchema schema, string? connectionId = null, CancellationToken ct = default)
    {
        try
        {
            var schemaIndexer = _serviceFactory.GetSchemaIndexer();
            var fingerprint = CreateSimpleFingerprint(schema);

            // ✅ PHASE-1 TASK-03: Use Redis for distributed caching (multi-instance safe)
            string cacheKey = $"TextToSqlAgent:SchemaIndexed:{fingerprint.Hash}";

            // Check Redis cache first (avoid Qdrant network call)
            if (_distributedCache != null)
            {
                var cachedValue = await _distributedCache.GetStringAsync(cacheKey, ct);
                if (cachedValue == "true")
                {
                    _logger.LogInformation(
                        "[SchemaRetrieval] ✅ Schema indexing status cached in Redis (hash: {Hash}), skipping Qdrant check",
                        fingerprint.Hash.Substring(0, 8));
                    return;
                }
            }

            // ✅ PHASE-2 TASK 2.1: Check if schema is already indexed before re-indexing
            var isIndexed = await schemaIndexer.IsSchemaIndexedAsync(fingerprint, ct);
            if (isIndexed)
            {
                _logger.LogInformation(
                    "[SchemaRetrieval] ✓ Schema already indexed (fingerprint match: {Hash}), skipping re-index",
                    fingerprint.Hash.Substring(0, 8));

                // ✅ PHASE-1 TASK-03: Cache result in Redis for 5 minutes
                if (_distributedCache != null)
                {
                    var options = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    };
                    await _distributedCache.SetStringAsync(cacheKey, "true", options, ct);
                    _logger.LogInformation("[SchemaRetrieval] ✅ Cached indexing status in Redis (TTL: 5min)");
                }
                return;
            }

            _logger.LogInformation("[SchemaRetrieval] Schema not indexed or fingerprint mismatch, indexing now...");
            if (connectionId != null)
            {
                await schemaIndexer.IndexSchemaAsync(schema, fingerprint, connectionId, ct);
            }
            else
            {
                await schemaIndexer.IndexSchemaAsync(schema, fingerprint, ct);
            }
            _logger.LogInformation("[SchemaRetrieval] ✓ Schema indexed successfully");

            // ✅ PHASE-1 TASK-03: Cache successful indexing in Redis
            if (_distributedCache != null)
            {
                var options = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _distributedCache.SetStringAsync(cacheKey, "true", options, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SchemaRetrieval] Schema indexing failed, RAG may not work optimally");
        }
    }

    private static SchemaFingerprint CreateSimpleFingerprint(DatabaseSchema schema)
    {
        var normalizedTables = schema.Tables
            .OrderBy(t => t.Schema)
            .ThenBy(t => t.TableName)
            .Select(table =>
                $"{table.Schema}.{table.TableName}:" +
                string.Join(
                    ",",
                    table.Columns
                        .OrderBy(c => c.ColumnName)
                        .Select(column =>
                            $"{column.ColumnName}:{column.DataType}:{column.IsPrimaryKey}:{column.IsForeignKey}")))
            .ToList();

        var normalizedRelationships = schema.Relationships
            .OrderBy(r => r.FromTable)
            .ThenBy(r => r.FromColumn)
            .ThenBy(r => r.ToTable)
            .ThenBy(r => r.ToColumn)
            .Select(r => $"{r.FromTable}.{r.FromColumn}>{r.ToTable}.{r.ToColumn}")
            .ToList();

        var normalizedSchema = string.Join("|", normalizedTables) + "||" + string.Join("|", normalizedRelationships);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSchema));

        return new SchemaFingerprint
        {
            Hash = Convert.ToHexString(hashBytes),
            ComputedAt = DateTime.UtcNow,
            TableCount = schema.Tables.Count,
            ColumnCount = schema.Tables.Sum(t => t.Columns.Count),
            RelationshipCount = schema.Relationships.Count,
            TableNames = schema.Tables.Select(t => t.TableName).OrderBy(n => n).ToList()
        };
    }

    private static string? ExtractDatabaseName(DatabaseConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString)) return null;

        var parts = config.ConnectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        static string? GetValue(string[] items, params string[] keys)
        {
            foreach (var key in keys)
            {
                var match = items.FirstOrDefault(p =>
                    p.Trim().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    var kv = match.Split('=', 2);
                    if (kv.Length == 2) return kv[1].Trim();
                }
            }
            return null;
        }

        var database = GetValue(parts, "Database", "Initial Catalog");
        if (!string.IsNullOrWhiteSpace(database)) return database;

        return GetValue(parts, "Server", "Host", "Data Source", "DataSource");
    }

    private static RetrievedSchemaContext BuildFallbackSchema(string targetTable, DatabaseSchema fullSchema)
    {
        var context = new RetrievedSchemaContext();
        var table = fullSchema.Tables.FirstOrDefault(t =>
            t.TableName.Equals(targetTable, StringComparison.OrdinalIgnoreCase));

        if (table != null)
        {
            context.RelevantTables.Add(table);
            context.TableColumns[table.TableName] = table.Columns;

            var relatedRels = fullSchema.Relationships.Where(r =>
                r.FromTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase) ||
                r.ToTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var rel in relatedRels)
            {
                context.RelevantRelationships.Add(rel);
                var relatedTableName = rel.FromTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase)
                    ? rel.ToTable : rel.FromTable;

                var parts = relatedTableName.Split('.');
                var tableName = parts.Length > 1 ? parts[1] : parts[0];

                var relatedTable = fullSchema.Tables.FirstOrDefault(t =>
                    t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

                if (relatedTable != null && !context.RelevantTables.Contains(relatedTable))
                {
                    context.RelevantTables.Add(relatedTable);
                    context.TableColumns[relatedTable.TableName] = relatedTable.Columns;
                }
            }
        }

        return context;
    }

    #endregion
}
