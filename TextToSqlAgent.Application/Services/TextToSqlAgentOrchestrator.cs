using Microsoft.Extensions.Logging;

// Actually, Orchestrator uses AnsiConsole in TryEnsureSchemaIndexedAsync. I need to remove UI logic from here.
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Application.Services;

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
                        // Removed AnsiConsole calls to decouple from Console UI
                        _logger.LogWarning("⚠️  Warning: RAG is not available. Using full schema. Reason: Could not connect to Qdrant vector database.");
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
            // STEP 4: Intent Analysis (with RAG context)
            // ====================================
            steps.Add("Step 4: Analyze intent");

            // ========================================
            // FALLBACK: If RAG found nothing, use full schema
            // ========================================
            IntentAnalysis intent;
            if (relevantSchema.RelevantTables.Count == 0)
            {
                _logger.LogWarning("[Agent] RAG found 0 results, falling back to full schema");

                // Use all table names for intent analysis
                var allTableNames = _cachedSchema.Tables.Select(t => t.TableName).ToList();

                // Single intent analysis call (reuse the result)
                intent = await _intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    allTableNames,
                    cancellationToken);

                // Build relevant schema based on intent target
                relevantSchema = BuildFallbackSchema(intent.Target, _cachedSchema);

                _logger.LogInformation(
                    "[Agent] Fallback schema: {Tables} tables",
                    relevantSchema.RelevantTables.Count);
            }
            else
            {
                // Normal path: use RAG-retrieved tables for intent analysis
                var tableNames = relevantSchema.RelevantTables.Select(t => t.TableName).ToList();
                intent = await _intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    tableNames,
                    cancellationToken);
            }

            if (intent.NeedsClarification)
            {
                response.Success = false;
                response.Answer = intent.ClarificationQuestion ?? "Question is unclear.";
                response.ProcessingSteps = steps;
                return response;
            }

            // ====================================
            // STEP 5: Generate SQL (with RAG context + original question)
            // ====================================
            steps.Add("Step 5: Generate SQL with RAG context");

            // ✅ Get SQL + suggestions in one API call
            var sqlResult = await _sqlGenerator.GenerateSqlWithContextAsync(
                intent,
                relevantSchema,
                normalized.NormalizedText,  // Pass original question to LLM
                null,  // No conversation history for legacy orchestrator
                structuredConversationContext: null,
                cancellationToken: cancellationToken);

            var sql = sqlResult.Sql;

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

            // ====================================
            // STEP 7: Execute SQL with Self-Correction
            // ====================================
            steps.Add("Step 7: Execute SQL with self-correction");
            var correctionResult = await ExecuteWithSelfCorrectionAsync(
                sql,
                relevantSchema,
                intent,
                cancellationToken);
            var executionResult = correctionResult.Result;
            var corrections = correctionResult.Corrections;

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

            // Apply EnsureLimit on the final executed SQL (after correction)
            var finalSql = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            finalSql = _sqlGenerator.EnsureLimit(finalSql);

            // ====================================
            // STEP 8: Format Answer
            // ====================================
            steps.Add("Step 8: Interpret results");
            var answer = FormatAnswer(intent, executionResult, corrections, normalized.NormalizedText);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = finalSql;
            response.QueryResult = executionResult;
            response.ProcessingSteps = steps;

            // ✅ Add suggestions from LLM response
            response.SuggestedQueries = sqlResult.SuggestedQueries;

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

    public async Task<AgentResponse> ProcessQueryAsync(
        string userQuestion,
        string userId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var response = new AgentResponse();
        var steps = new List<string>();

        try
        {
            _logger.LogDebug("[Agent] Processing new question for user {UserId}, connection {ConnectionId}", userId, connectionId);

            // ====================================
            // STEP 1: Normalize Prompt
            // ====================================
            steps.Add("Step 1: Normalize question");
            var normalized = await _normalizeTask.ExecuteAsync(userQuestion, cancellationToken);

            // ====================================
            // STEP 1.5: Setup Qdrant Collection Name (per-user)
            // ====================================
            try
            {
                _qdrantService.SetUserCollectionName(userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Agent] Cannot set user collection name. Using default collection name.");
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
                    if (!await TryEnsureSchemaIndexedAsync(_cachedSchema, connectionId, cancellationToken))
                    {
                        _logger.LogWarning("⚠️  Warning: RAG is not available. Using full schema. Reason: Could not connect to Qdrant vector database.");
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
                connectionId,
                cancellationToken);

            _logger.LogDebug(
                "[Agent] RAG found: {Tables} tables, {Rels} relationships",
                relevantSchema.RelevantTables.Count,
                relevantSchema.RelevantRelationships.Count);

            // ====================================
            // STEP 4: Intent Analysis (with RAG context)
            // ====================================
            steps.Add("Step 4: Analyze intent");

            // ========================================
            // FALLBACK: If RAG found nothing, use full schema
            // ========================================
            IntentAnalysis intent;
            if (relevantSchema.RelevantTables.Count == 0)
            {
                _logger.LogWarning("[Agent] RAG found 0 results, falling back to full schema");

                // Use all table names for intent analysis
                var allTableNames = _cachedSchema.Tables.Select(t => t.TableName).ToList();

                // Single intent analysis call (reuse the result)
                intent = await _intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    allTableNames,
                    cancellationToken);

                // Build relevant schema based on intent target
                relevantSchema = BuildFallbackSchema(intent.Target, _cachedSchema);

                _logger.LogInformation(
                    "[Agent] Fallback schema: {Tables} tables",
                    relevantSchema.RelevantTables.Count);
            }
            else
            {
                // Normal path: use RAG-retrieved tables for intent analysis
                var tableNames = relevantSchema.RelevantTables.Select(t => t.TableName).ToList();
                intent = await _intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    tableNames,
                    cancellationToken);
            }

            if (intent.NeedsClarification)
            {
                response.Success = false;
                response.Answer = intent.ClarificationQuestion ?? "Question is unclear.";
                response.ProcessingSteps = steps;
                return response;
            }

            // ====================================
            // STEP 5: Generate SQL (with RAG context + original question)
            // ====================================
            steps.Add("Step 5: Generate SQL with RAG context");

            // ✅ Get SQL + suggestions in one API call
            var sqlResult = await _sqlGenerator.GenerateSqlWithContextAsync(
                intent,
                relevantSchema,
                normalized.NormalizedText,  // Pass original question to LLM
                null,  // No conversation history for legacy orchestrator
                structuredConversationContext: null,
                cancellationToken: cancellationToken);

            var sql = sqlResult.Sql;

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

            // ====================================
            // STEP 7: Execute SQL with Self-Correction
            // ====================================
            steps.Add("Step 7: Execute SQL with self-correction");
            var correctionResult = await ExecuteWithSelfCorrectionAsync(
                sql,
                relevantSchema,
                intent,
                cancellationToken);
            var executionResult = correctionResult.Result;
            var corrections = correctionResult.Corrections;

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

            // Apply EnsureLimit on the final executed SQL (after correction)
            var finalSql = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            finalSql = _sqlGenerator.EnsureLimit(finalSql);

            // ====================================
            // STEP 8: Format Answer
            // ====================================
            steps.Add("Step 8: Interpret results");
            var answer = FormatAnswer(intent, executionResult, corrections, normalized.NormalizedText);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = finalSql;
            response.QueryResult = executionResult;
            response.ProcessingSteps = steps;

            // ✅ Add suggestions from LLM response
            response.SuggestedQueries = sqlResult.SuggestedQueries;

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

        // SQL Server only: use database name from connection string
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
    IntentAnalysis intent,
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
                intent,
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
        List<CorrectionAttempt> corrections,
        string? originalQuestion = null)
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

        // Build rich answer based on intent type
        answer += intent.Intent switch
        {
            QueryIntent.COUNT => FormatCountAnswer(result),
            QueryIntent.LIST => FormatListAnswer(result, intent),
            QueryIntent.SCHEMA when intent.Target.Equals("TABLES", StringComparison.OrdinalIgnoreCase) =>
                FormatSchemaAnswer(result),
            QueryIntent.AGGREGATE or QueryIntent.SUM or QueryIntent.AVG or QueryIntent.GROUP_BY =>
                FormatAggregateAnswer(result, intent),
            QueryIntent.TOP_N => FormatTopNAnswer(result, intent),
            QueryIntent.DETAIL => FormatDetailAnswer(result),
            QueryIntent.RANKING => FormatRankingAnswer(result, intent),
            QueryIntent.COMPARISON => FormatComparisonAnswer(result),
            _ => FormatGenericAnswer(result)
        };

        return answer;
    }

    private static string FormatCountAnswer(SqlExecutionResult result)
    {
        var firstRow = result.Rows[0];
        var countValue = firstRow.Values.First();
        return $"Count: {countValue} records.";
    }

    private static string FormatListAnswer(SqlExecutionResult result, IntentAnalysis intent)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {result.RowCount} results.");

        // Show preview of first 3 rows with key columns
        var previewCount = Math.Min(result.RowCount, 3);
        if (result.Columns.Count > 0 && previewCount > 0)
        {
            sb.AppendLine("Preview:");
            // Pick the most meaningful columns (skip IDs)
            var displayCols = result.Columns
                .Where(c => !c.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                         || result.Columns.Count <= 3)
                .Take(4)
                .ToList();

            if (!displayCols.Any()) displayCols = result.Columns.Take(4).ToList();

            for (int i = 0; i < previewCount; i++)
            {
                var row = result.Rows[i];
                var vals = displayCols
                    .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                    .Select(c => $"{c}: {FormatValue(row[c])}")
                    .ToList();
                sb.AppendLine($"  • {string.Join(" | ", vals)}");
            }

            if (result.RowCount > previewCount)
                sb.AppendLine($"  ... and {result.RowCount - previewCount} more.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSchemaAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Database contains {result.RowCount} tables:");

        foreach (var row in result.Rows.Take(20))
        {
            var tableName = row.Values.FirstOrDefault()?.ToString() ?? "Unknown";
            sb.AppendLine($"  • {tableName}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatAggregateAnswer(SqlExecutionResult result, IntentAnalysis intent)
    {
        var sb = new System.Text.StringBuilder();

        if (result.RowCount == 1)
        {
            // Single aggregate result
            var row = result.Rows[0];
            var parts = result.Columns
                .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                .Select(c => $"{c}: {FormatValue(row[c])}")
                .ToList();
            sb.AppendLine(string.Join(" | ", parts));
        }
        else
        {
            // Grouped results
            sb.AppendLine($"Analysis result: {result.RowCount} groups.");
            var previewCount = Math.Min(result.RowCount, 5);
            for (int i = 0; i < previewCount; i++)
            {
                var row = result.Rows[i];
                var vals = result.Columns
                    .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                    .Select(c => $"{c}: {FormatValue(row[c])}")
                    .ToList();
                sb.AppendLine($"  • {string.Join(" | ", vals)}");
            }
            if (result.RowCount > previewCount)
                sb.AppendLine($"  ... and {result.RowCount - previewCount} more groups.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatTopNAnswer(SqlExecutionResult result, IntentAnalysis intent)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Top {result.RowCount} results:");

        for (int i = 0; i < result.RowCount; i++)
        {
            var row = result.Rows[i];
            var displayCols = result.Columns
                .Where(c => !c.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList();
            if (!displayCols.Any()) displayCols = result.Columns.Take(4).ToList();

            var vals = displayCols
                .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                .Select(c => $"{FormatValue(row[c])}")
                .ToList();
            sb.AppendLine($"  #{i + 1}: {string.Join(" | ", vals)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatDetailAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Detail info ({result.RowCount} record{(result.RowCount > 1 ? "s" : "")}):");

        foreach (var row in result.Rows.Take(5))
        {
            foreach (var col in result.Columns)
            {
                if (row.ContainsKey(col) && row[col] != null && row[col] != DBNull.Value)
                {
                    sb.AppendLine($"  {col}: {FormatValue(row[col])}");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatRankingAnswer(SqlExecutionResult result, IntentAnalysis intent)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Ranking ({result.RowCount} entries):");

        for (int i = 0; i < Math.Min(result.RowCount, 10); i++)
        {
            var row = result.Rows[i];
            var vals = result.Columns
                .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                .Take(4)
                .Select(c => $"{FormatValue(row[c])}")
                .ToList();
            sb.AppendLine($"  #{i + 1}: {string.Join(" | ", vals)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatComparisonAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Comparison result ({result.RowCount} periods):");

        foreach (var row in result.Rows.Take(12))
        {
            var vals = result.Columns
                .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                .Select(c => $"{c}: {FormatValue(row[c])}")
                .ToList();
            sb.AppendLine($"  • {string.Join(" | ", vals)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatGenericAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Query successful, returned {result.RowCount} results.");

        // Show preview
        var previewCount = Math.Min(result.RowCount, 3);
        if (previewCount > 0 && result.Columns.Count > 0)
        {
            var displayCols = result.Columns.Take(5).ToList();
            for (int i = 0; i < previewCount; i++)
            {
                var row = result.Rows[i];
                var vals = displayCols
                    .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                    .Select(c => $"{FormatValue(row[c])}")
                    .ToList();
                sb.AppendLine($"  • {string.Join(" | ", vals)}");
            }
            if (result.RowCount > previewCount)
                sb.AppendLine($"  ... and {result.RowCount - previewCount} more.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            decimal d => d.ToString("N2"),
            double d => d.ToString("N2"),
            float f => f.ToString("N2"),
            _ => value.ToString() ?? ""
        };
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
        return await TryEnsureSchemaIndexedAsync(schema, null, cancellationToken);
    }

    private async Task<bool> TryEnsureSchemaIndexedAsync(
    DatabaseSchema schema,
    string? connectionId,
    CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[Agent] Checking Qdrant collection...");

            // Use timeout to avoid hanging when Qdrant is not available
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10 second timeout

            try
            {
                await _qdrantService.EnsureCollectionAsync(cts.Token);
                var pointCount = await _qdrantService.GetPointCountAsync(cts.Token);

                if (pointCount == 0)
                {
                    _logger.LogInformation("[Agent] Indexing schema to Qdrant...");
                    var fingerprint = CreateSimpleFingerprint(schema);
                    await _schemaIndexer.IndexSchemaAsync(schema, fingerprint, connectionId, cts.Token);
                    _logger.LogInformation("[Agent] ✓ Schema indexed");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("[Agent] Qdrant not available or timeout - skipping vector indexing");
                return false; // Continue without Qdrant
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Agent] Error indexing schema to Qdrant - continuing without it");
            return false;
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
}

