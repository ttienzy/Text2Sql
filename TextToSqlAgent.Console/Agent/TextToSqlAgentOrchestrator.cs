using Microsoft.Extensions.Logging;
using Spectre.Console;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Console.Agent;

public class TextToSqlAgentOrchestrator
{
    private readonly NormalizePromptTask _normalizeTask;
    private readonly IntentAnalysisPlugin _intentPlugin;
    private readonly SchemaScanner _schemaScanner;
    private readonly SchemaIndexer _schemaIndexer;
    private readonly SchemaRetriever _schemaRetriever;
    private readonly QdrantService _qdrantService;
    private readonly SqlGeneratorPlugin _sqlGenerator;
    private readonly SqlCorrectorPlugin _sqlCorrector;
    private readonly SqlExecutor _sqlExecutor;
    private readonly AgentConfig _agentConfig;
    private readonly DatabaseConfig _dbConfig;
    private readonly ILogger<TextToSqlAgentOrchestrator> _logger;

    private DatabaseSchema? _cachedSchema;
    private bool _schemaIndexed = false;

    public TextToSqlAgentOrchestrator(
        NormalizePromptTask normalizeTask,
        IntentAnalysisPlugin intentPlugin,
        SchemaScanner schemaScanner,
        SchemaIndexer schemaIndexer,
        SchemaRetriever schemaRetriever,
        QdrantService qdrantService,
        SqlGeneratorPlugin sqlGenerator,
        SqlCorrectorPlugin sqlCorrector,
        SqlExecutor sqlExecutor,
        AgentConfig agentConfig,
        DatabaseConfig dbConfig,
        ILogger<TextToSqlAgentOrchestrator> logger)
    {
        _normalizeTask = normalizeTask;
        _intentPlugin = intentPlugin;
        _schemaScanner = schemaScanner;
        _schemaIndexer = schemaIndexer;
        _schemaRetriever = schemaRetriever;
        _qdrantService = qdrantService;
        _sqlGenerator = sqlGenerator;
        _sqlCorrector = sqlCorrector;
        _sqlExecutor = sqlExecutor;
        _agentConfig = agentConfig;
        _dbConfig = dbConfig;
        _logger = logger;
    }

    public async Task<AgentResponse> ProcessQueryAsync(
        string userQuestion,
        CancellationToken cancellationToken = default)
    {
        var response = new AgentResponse();
        var steps = new List<string>();

        try
        {
            _logger.LogDebug("[Agent] Processing new question");

            // ====================================
            // STEP 1: Normalize Prompt
            // ====================================
            steps.Add("Step 1: Normalize question");
            var normalized = await _normalizeTask.ExecuteAsync(userQuestion, cancellationToken);

            // ====================================
            // STEP 1.5: Setup Qdrant Collection Name (provider-agnostic)
            // ====================================
            try
            {
                var dbName = ExtractDatabaseName(_dbConfig);
                if (!string.IsNullOrEmpty(dbName))
                {
                    _qdrantService.SetCollectionName(dbName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Agent] Cannot extract database name from connection string. Using default collection name.");
            }

            // ====================================
            // STEP 2: Scan Schema (if not cached)
            // ====================================
            if (_cachedSchema == null)
            {
                steps.Add("Step 2: Scan database schema");

                try
                {
                    _cachedSchema = await _schemaScanner.ScanAsync(cancellationToken);
                }
                catch (DatabaseConnectionException ex)
                {
                    _logger.LogError(ex, "[Agent] Cannot connect to database");
                    response.Success = false;
                    response.ErrorMessage = "Cannot connect to database. Please check your connection string.";
                    response.ProcessingSteps = steps;
                    return response;
                }
                catch (DatabasePermissionException ex)
                {
                    _logger.LogError(ex, "[Agent] Insufficient database permissions");
                    response.Success = false;
                    response.ErrorMessage = "Insufficient database permissions. Please grant SELECT on INFORMATION_SCHEMA.";
                    response.ProcessingSteps = steps;
                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Agent] Failed to scan database schema");
                    response.Success = false;
                    response.ErrorMessage = $"Failed to scan database schema: {ex.Message}";
                    response.ProcessingSteps = steps;
                    return response;
                }

                // Auto-index schema after first scan
                if (!_schemaIndexed)
                {
                    steps.Add("Step 2.5: Index schema into vector database");

                    // Use TryEnsure instead of Ensure to prevent crash
                    if (!await TryEnsureSchemaIndexedAsync(_cachedSchema, cancellationToken))
                    {
                        AnsiConsole.MarkupLine("[yellow]⚠️  Warning: RAG is not available. Using full schema.[/]");
                        AnsiConsole.MarkupLine("[dim]   Reason: Could not connect to Qdrant vector database[/]");
                        AnsiConsole.MarkupLine("[dim]   Impact: Queries may be slower and less accurate[/]");
                    }

                    _schemaIndexed = true;
                }
            }
            else
            {
                steps.Add("Step 2: Use cached schema");
                _logger.LogDebug("[Agent] Using cached schema");
            }

            // ====================================
            // STEP 3: RAG - Retrieve Relevant Schema
            // ====================================
            steps.Add("Step 3: RAG - Retrieve relevant schema");
            var relevantSchema = await _schemaRetriever.RetrieveAsync(
                normalized.NormalizedText,
                _cachedSchema,
                cancellationToken);

            _logger.LogDebug(
                "[Agent] RAG found: {Tables} tables, {Rels} relationships",
                relevantSchema.RelevantTables.Count,
                relevantSchema.RelevantRelationships.Count);

            // ====================================
            // STEP 4: Intent Analysis (với RAG context)
            // ====================================

            // ========================================
            // FALLBACK: Nếu RAG không tìm thấy gì
            // ========================================
            if (relevantSchema.RelevantTables.Count == 0)
            {
                _logger.LogWarning("[Agent] RAG found 0 results, falling back to full schema");

                // Get table names for intent analysis
                var tableNamess = _cachedSchema.Tables.Select(t => t.TableName).ToList();

                // Continue with intent analysis...
                var intents = await _intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    tableNamess,
                    cancellationToken);

                // Build relevant schema based on intent target
                relevantSchema = BuildFallbackSchema(intents.Target, _cachedSchema);

                _logger.LogInformation(
                    "[Agent] Fallback schema: {Tables} tables",
                    relevantSchema.RelevantTables.Count);
            }
            steps.Add("Step 4: Analyze intent");
            var tableNames = relevantSchema.RelevantTables.Select(t => t.TableName).ToList();
            var intent = await _intentPlugin.AnalyzeIntentAsync(
                normalized.NormalizedText,
                tableNames,
                cancellationToken);

            if (intent.NeedsClarification)
            {
                response.Success = false;
                response.Answer = intent.ClarificationQuestion ?? "Question is unclear.";
                response.ProcessingSteps = steps;
                return response;
            }

            // ====================================
            // STEP 5: Generate SQL (với RAG context)
            // ====================================
            steps.Add("Step 5: Generate SQL with RAG context");
            var sql = await _sqlGenerator.GenerateSqlWithContextAsync(
                intent,
                relevantSchema,
                cancellationToken);

            // ====================================
            // STEP 6: Validate SQL
            // ====================================
            steps.Add("Step 6: Validate SQL");
            if (!_sqlGenerator.ValidateSql(sql))
            {
                response.Success = false;
                response.ErrorMessage = "Unsafe SQL detected";
                response.SqlGenerated = sql;
                response.ProcessingSteps = steps;
                return response;
            }

            sql = _sqlGenerator.EnsureLimit(sql);

            // ====================================
            // STEP 7: Execute SQL với Self-Correction
            // ====================================
            steps.Add("Step 7: Execute SQL with self-correction");
            var (executionResult, corrections) = await ExecuteWithSelfCorrectionAsync(
                sql,
                relevantSchema,
                intent,
                cancellationToken);

            response.CorrectionHistory = corrections;
            response.WasCorrected = corrections.Any();
            response.CorrectionAttempts = corrections.Count;

            if (!executionResult.Success)
            {
                response.Success = false;
                response.ErrorMessage = executionResult.ErrorMessage;
                response.SqlGenerated = sql;
                response.QueryResult = executionResult;
                response.ProcessingSteps = steps;
                return response;
            }

            // ====================================
            // STEP 8: Format Answer
            // ====================================
            steps.Add("Step 8: Interpret results");
            var answer = FormatAnswer(intent, executionResult, corrections);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            response.QueryResult = executionResult;
            response.ProcessingSteps = steps;

            _logger.LogInformation("[Agent] ✓ Processing complete");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Agent] Error processing query");

            response.Success = false;
            response.ErrorMessage = $"Error: {ex.Message}";
            response.ProcessingSteps = steps;

            return response;
        }
    }

    private static string? ExtractDatabaseName(DatabaseConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            return null;
        }

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
                    if (kv.Length == 2)
                    {
                        return kv[1].Trim();
                    }
                }
            }

            return null;
        }

        // SQLite: use file name (without extension) as logical database/collection name
        if (config.Provider == DatabaseProvider.SQLite)
        {
            var dataSource = GetValue(parts, "Data Source", "DataSource", "Filename", "File");
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                return null;
            }

            try
            {
                var fullPath = System.IO.Path.GetFullPath(dataSource);
                return System.IO.Path.GetFileNameWithoutExtension(fullPath);
            }
            catch
            {
                return dataSource;
            }
        }

        // Other providers: prefer Database / Initial Catalog
        var database = GetValue(parts, "Database", "Initial Catalog");
        if (!string.IsNullOrWhiteSpace(database))
        {
            return database;
        }

        // Fallback: try to infer from data source/host
        var host = GetValue(parts, "Server", "Host", "Data Source", "DataSource");
        return host;
    }
    private RetrievedSchemaContext BuildFallbackSchema(string targetTable, DatabaseSchema fullSchema)
    {
        var context = new RetrievedSchemaContext();

        // Find target table
        var table = fullSchema.Tables.FirstOrDefault(t =>
            t.TableName.Equals(targetTable, StringComparison.OrdinalIgnoreCase));

        if (table != null)
        {
            context.RelevantTables.Add(table);
            context.TableColumns[table.TableName] = table.Columns;

            // Find related tables via FK
            var relatedRels = fullSchema.Relationships.Where(r =>
                r.FromTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase) ||
                r.ToTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var rel in relatedRels)
            {
                context.RelevantRelationships.Add(rel);

                // Add related table
                var relatedTableName = rel.FromTable.Contains(table.TableName, StringComparison.OrdinalIgnoreCase)
                    ? rel.ToTable
                    : rel.FromTable;

                var relatedTable = fullSchema.Tables.FirstOrDefault(t =>
                    t.TableName.Equals(ExtractTableName(relatedTableName), StringComparison.OrdinalIgnoreCase));

                if (relatedTable != null && !context.RelevantTables.Contains(relatedTable))
                {
                    context.RelevantTables.Add(relatedTable);
                    context.TableColumns[relatedTable.TableName] = relatedTable.Columns;
                }
            }
        }

        return context;
    }

    private string ExtractTableName(string fullName)
    {
        var parts = fullName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    // ====================================
    // SELF-CORRECTION LOOP
    // ====================================
    private async Task<(SqlExecutionResult Result, List<CorrectionAttempt> Corrections)> ExecuteWithSelfCorrectionAsync(
    string initialSql,
    RetrievedSchemaContext schemaContext,
    IntentAnalysis intent,  // ← ADD
    CancellationToken cancellationToken)
    {
        var corrections = new List<CorrectionAttempt>();
        var currentSql = initialSql;
        var attemptNumber = 0;

        while (attemptNumber < _agentConfig.MaxSelfCorrectionAttempts)
        {
            _logger.LogDebug("[Agent] Executing SQL (Attempt {Attempt})", attemptNumber + 1);

            // Execute
            var result = await _sqlExecutor.ExecuteAsync(currentSql, cancellationToken);

            // Success!
            if (result.Success)
            {
                if (corrections.Any())
                {
                    _logger.LogInformation("[Agent] ✓ SQL auto-corrected and executed successfully after {Count} attempts",
                        attemptNumber);
                }
                return (result, corrections);
            }

            // Failed - try to correct
            _logger.LogWarning("[Agent] SQL Error: {Error}", result.ErrorMessage);

            attemptNumber++;

            if (attemptNumber >= _agentConfig.MaxSelfCorrectionAttempts)
            {
                _logger.LogError("[Agent] Max self-correction attempts reached ({Max})", _agentConfig.MaxSelfCorrectionAttempts);
                return (result, corrections);
            }

            // Attempt correction
            _logger.LogDebug("[Agent] Attempting auto-correction...");

            var correction = await _sqlCorrector.CorrectSqlAsync(
                currentSql,
                result.ErrorMessage ?? "Unknown error",
                schemaContext,
                intent,  // ← PASS intent
                attemptNumber,
                cancellationToken);

            corrections.Add(correction);

            if (!correction.Success)
            {
                _logger.LogWarning("[Agent] Unable to auto-correct SQL error");
                return (result, corrections);
            }

            if (!_sqlCorrector.ShouldRetry(corrections, _agentConfig.MaxSelfCorrectionAttempts))
            {
                _logger.LogWarning("[Agent] Stopping retry loop");
                return (result, corrections);
            }

            // Use corrected SQL for next attempt
            currentSql = correction.CorrectedSql;

            _logger.LogDebug("[Agent] Retrying with corrected SQL...");
        }

        var finalResult = await _sqlExecutor.ExecuteAsync(currentSql, cancellationToken);
        return (finalResult, corrections);
    }

    private string FormatAnswer(
        IntentAnalysis intent,
        SqlExecutionResult result,
        List<CorrectionAttempt> corrections)
    {
        var answer = "";

        // Add correction info if any
        if (corrections.Any())
        {
            answer += $"ℹ️  SQL was auto-corrected {corrections.Count} times.\n";
        }
        if (result.RowCount == 0)
        {
            return answer + "No results found.";
        }
        answer += intent.Intent switch
        {
            QueryIntent.COUNT => $"Count: {result.Rows[0].Values.First()} records.",
            QueryIntent.LIST => $"Found {result.RowCount} results.",
            QueryIntent.SCHEMA when intent.Target.Equals("TABLES", StringComparison.OrdinalIgnoreCase) =>
                $"Database contains {result.RowCount} tables.",
            QueryIntent.AGGREGATE => $"Analysis result: {result.RowCount} groups.",
            QueryIntent.DETAIL => $"Detail info: {result.RowCount} records.",
            _ => $"Query successful, returned {result.RowCount} results."
        }; return answer;
    }
    public void ClearSchemaCache()
    {
        _cachedSchema = null;
        _schemaIndexed = false;
        _logger.LogInformation("[Agent] Schema cache cleared");
    }

    private async Task<bool> TryEnsureSchemaIndexedAsync(
    DatabaseSchema schema,
    CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[Agent] Checking Qdrant collection...");

            await _qdrantService.EnsureCollectionAsync(cancellationToken);
            var pointCount = await _qdrantService.GetPointCountAsync(cancellationToken);

            if (pointCount == 0)
            {
                _logger.LogInformation("[Agent] Indexing schema to Qdrant...");
                await _schemaIndexer.IndexSchemaAsync(schema, cancellationToken);
                _logger.LogInformation("[Agent] ✓ Schema indexed");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Agent] Error indexing schema");
            return false;
        }
    }
}