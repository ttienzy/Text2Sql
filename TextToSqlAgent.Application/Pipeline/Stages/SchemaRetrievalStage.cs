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
        ISchemaCache? schemaCache = null)
    {
        _serviceFactory = serviceFactory;
        _dbConfig = dbConfig;
        _logger = logger;
        _schemaCache = schemaCache;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // ── Step 1: Load / scan database schema ──
        context.Steps.Add("Load database schema");

        await EnsureSchemaLoadedAsync(context.Steps, ct);
        context.Schema = _cachedSchema;
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
        IntentAnalysis intent;
        var intentAnalyzer = _serviceFactory.GetIntentAnalyzer();

        if (relevantSchema.RelevantTables.Count == 0)
        {
            _logger.LogWarning("[SchemaRetrieval] RAG found 0 results, using fallback");

            intent = await intentAnalyzer.AnalyzeIntentAsync(queryText, context.TableNames, ct);
            relevantSchema = BuildFallbackSchema(intent.Target, _cachedSchema!);
        }
        else
        {
            context.Steps.Add("Analyze intent");
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
            await TryEnsureSchemaIndexedAsync(_cachedSchema, ct);
            _schemaIndexed = true;
        }
    }

    private async Task TryEnsureSchemaIndexedAsync(DatabaseSchema schema, CancellationToken ct)
    {
        try
        {
            var schemaIndexer = _serviceFactory.GetSchemaIndexer();
            var fingerprint = CreateSimpleFingerprint(schema);
            await schemaIndexer.IndexSchemaAsync(schema, fingerprint, ct);
            _logger.LogInformation("[SchemaRetrieval] Schema indexed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SchemaRetrieval] Schema indexing failed, RAG may not work optimally");
        }
    }

    private static SchemaFingerprint CreateSimpleFingerprint(DatabaseSchema schema)
    {
        return new SchemaFingerprint
        {
            Hash = Guid.NewGuid().ToString(), // Simple placeholder hash
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
