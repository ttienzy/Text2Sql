using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Enhanced Agentic AI Orchestrator with:
/// - Query validation and routing
/// - Multi-turn conversation support
/// - Query explanation
/// - Intelligent error handling
/// - Context-aware processing
/// - LAZY LOADING for fast startup
/// </summary>
public class EnhancedAgentOrchestrator
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly AgentConfig _agentConfig;
    private readonly DatabaseConfig _dbConfig;
    private readonly ILogger<EnhancedAgentOrchestrator> _logger;

    private DatabaseSchema? _cachedSchema;
    private bool _schemaIndexed = false;

    public EnhancedAgentOrchestrator(
        IAgentServiceFactory serviceFactory,
        AgentConfig agentConfig,
        DatabaseConfig dbConfig,
        ILogger<EnhancedAgentOrchestrator> logger)
    {
        _serviceFactory = serviceFactory;
        _agentConfig = agentConfig;
        _dbConfig = dbConfig;
        _logger = logger;

        _logger.LogInformation("[EnhancedAgent] Initialized with lazy loading (fast startup mode)");
    }

    /// <summary>
    /// Process query with full agentic capabilities
    /// </summary>
    public async Task<AgentResponse> ProcessQueryAsync(
        string userQuestion,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        var response = new AgentResponse();
        var steps = new List<string>();

        try
        {
            _logger.LogInformation("[EnhancedAgent] 🤖 Processing query with agentic AI");

            // Get services on-demand (lazy loading)
            QueryValidatorPlugin queryValidator;
            ConversationManager conversationManager;
            NormalizePromptTask normalizeTask;

            try
            {
                queryValidator = _serviceFactory.GetQueryValidator();
                conversationManager = _serviceFactory.GetConversationManager();
                normalizeTask = _serviceFactory.GetOrCreate<NormalizePromptTask>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to create services: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to initialize services: {ex.Message}", ex);
            }

            // Get or create conversation context
            ConversationContext context;
            try
            {
                context = conversationManager.GetOrCreateContext(conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to create conversation context: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to create conversation context: {ex.Message}", ex);
            }

            // ====================================
            // STEP 0: Query Validation & Routing
            // ====================================
            steps.Add("Step 0: Validate query relevance");

            // PHASE 3 OPTIMIZATION: Validate WITHOUT schema first (fast path)
            // Only load schema if validation passes
            QueryValidationResult validation;
            try
            {
                validation = await queryValidator.ValidateQueryAsync(
                    userQuestion,
                    new List<string>(), // Empty list - validator will use heuristics only
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to validate query: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to validate query: {ex.Message}", ex);
            }

            _logger.LogInformation(
                "[EnhancedAgent] Query Type: {Type}, Relevant: {Relevant}, Confidence: {Confidence:P0}",
                validation.QueryType,
                validation.IsRelevant,
                validation.Confidence);

            // Handle non-database queries (FAST PATH - no schema loaded)
            if (!validation.IsRelevant)
            {
                response.Success = true;
                response.Answer = validation.SuggestedResponse ??
                    "I'm a database assistant. I can help you query your data. Please ask a database-related question.";
                response.ProcessingSteps = steps;

                // Add to conversation history
                conversationManager.AddTurn(
                    context,
                    userQuestion,
                    response.Answer,
                    success: true);

                return response;
            }

            // Handle clarification needed (FAST PATH - no schema loaded)
            if (validation.NeedsClarification)
            {
                response.Success = false;
                response.Answer = validation.ClarificationQuestion ?? "Please clarify your question.";
                response.ProcessingSteps = steps;

                conversationManager.AddTurn(
                    context,
                    userQuestion,
                    response.Answer,
                    success: false);

                return response;
            }

            // ====================================
            // STEP 1: Load Schema (ONLY for database queries)
            // ====================================
            steps.Add("Step 1: Load database schema");

            try
            {
                await EnsureSchemaLoadedAsync(steps, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to load schema: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to load database schema: {ex.Message}", ex);
            }

            var tableNames = _cachedSchema?.Tables.Select(t => t.TableName).ToList() ?? new List<string>();

            // ====================================
            // STEP 2: Context-Aware Normalization
            // ====================================
            steps.Add("Step 2: Normalize with conversation context");

            // Enrich question with context if it's a follow-up
            var enrichedQuestion = conversationManager.EnrichQuestionWithContext(context, userQuestion);

            NormalizedPrompt normalized;
            try
            {
                normalized = await normalizeTask.ExecuteAsync(enrichedQuestion, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to normalize prompt: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to normalize prompt: {ex.Message}", ex);
            }

            // ====================================
            // STEP 3: Setup Qdrant Collection
            // ====================================
            try
            {
                var dbName = ExtractDatabaseName(_dbConfig);
                if (!string.IsNullOrEmpty(dbName))
                {
                    var qdrantService = _serviceFactory.GetQdrantService();
                    qdrantService.SetCollectionName(dbName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EnhancedAgent] Cannot extract database name");
            }

            // ====================================
            // STEP 4: RAG - Retrieve Relevant Schema
            // ====================================
            steps.Add("Step 4: RAG - Retrieve relevant schema");

            var schemaRetriever = _serviceFactory.GetSchemaRetriever();
            var relevantSchema = await schemaRetriever.RetrieveAsync(
                normalized.NormalizedText,
                _cachedSchema!,
                cancellationToken);

            _logger.LogDebug(
                "[EnhancedAgent] RAG found: {Tables} tables, {Rels} relationships",
                relevantSchema.RelevantTables.Count,
                relevantSchema.RelevantRelationships.Count);

            // Fallback if RAG found nothing
            if (relevantSchema.RelevantTables.Count == 0)
            {
                _logger.LogWarning("[EnhancedAgent] RAG found 0 results, using fallback");

                var intentPlugin = _serviceFactory.GetIntentAnalyzer();
                var tempIntent = await intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    tableNames,
                    cancellationToken);

                relevantSchema = BuildFallbackSchema(tempIntent.Target, _cachedSchema);
            }

            // ====================================
            // STEP 5: Intent Analysis
            // ====================================
            steps.Add("Step 5: Analyze intent");

            var intentAnalyzer = _serviceFactory.GetIntentAnalyzer();
            var intent = await intentAnalyzer.AnalyzeIntentAsync(
                normalized.NormalizedText,
                relevantSchema.RelevantTables.Select(t => t.TableName).ToList(),
                cancellationToken);

            if (intent.NeedsClarification)
            {
                response.Success = false;
                response.Answer = intent.ClarificationQuestion ?? "Question is unclear.";
                response.ProcessingSteps = steps;

                conversationManager.AddTurn(
                    context,
                    userQuestion,
                    response.Answer,
                    intent: intent.Intent,
                    targetTable: intent.Target,
                    success: false);

                return response;
            }

            // ====================================
            // STEP 6: Generate SQL
            // ====================================
            steps.Add("Step 6: Generate SQL with RAG context");

            var sqlGenerator = _serviceFactory.GetSqlGenerator();
            var sql = await sqlGenerator.GenerateSqlWithContextAsync(
                intent,
                relevantSchema,
                cancellationToken);

            // ====================================
            // STEP 7: Validate SQL Safety
            // ====================================
            steps.Add("Step 7: Validate SQL safety");
            if (!sqlGenerator.ValidateSql(sql))
            {
                response.Success = false;
                response.ErrorMessage = "Unsafe SQL detected - only SELECT queries are allowed";
                response.SqlGenerated = sql;
                response.ProcessingSteps = steps;

                conversationManager.AddTurn(
                    context,
                    userQuestion,
                    response.ErrorMessage,
                    sqlQuery: sql,
                    intent: intent.Intent,
                    targetTable: intent.Target,
                    success: false);

                return response;
            }

            sql = sqlGenerator.EnsureLimit(sql);

            // ====================================
            // STEP 8: Explain Query (Optional)
            // ====================================
            if (_agentConfig.ExplainQueriesBeforeExecution)
            {
                steps.Add("Step 8: Explain query");

                var queryExplainer = _serviceFactory.GetQueryExplainer();
                var explanation = await queryExplainer.ExplainQueryAsync(
                    sql,
                    userQuestion,
                    cancellationToken);

                response.QueryExplanation = explanation;
                _logger.LogInformation("[EnhancedAgent] Query explanation: {Explanation}", explanation);
            }

            // ====================================
            // STEP 9: Execute with Self-Correction
            // ====================================
            steps.Add("Step 9: Execute SQL with self-correction");
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

                conversationManager.AddTurn(
                    context,
                    userQuestion,
                    $"Error: {executionResult.ErrorMessage}",
                    sqlQuery: sql,
                    intent: intent.Intent,
                    targetTable: intent.Target,
                    success: false);

                return response;
            }

            // ====================================
            // STEP 10: Format Answer
            // ====================================
            steps.Add("Step 10: Format intelligent answer");
            var answer = FormatIntelligentAnswer(intent, executionResult, corrections, context);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            response.QueryResult = executionResult;
            response.ProcessingSteps = steps;

            // Add to conversation history
            conversationManager.AddTurn(
                context,
                userQuestion,
                answer,
                sqlQuery: response.SqlGenerated,
                intent: intent.Intent,
                targetTable: intent.Target,
                success: true);

            _logger.LogInformation("[EnhancedAgent] ✅ Processing complete");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EnhancedAgent] Error processing query: {Message}", ex.Message);
            _logger.LogError(ex, "[EnhancedAgent] Exception type: {Type}", ex.GetType().FullName);
            _logger.LogError(ex, "[EnhancedAgent] Stack trace: {StackTrace}", ex.StackTrace);

            var errorMessage = FormatDetailedError(ex);
            _logger.LogInformation("[EnhancedAgent] Formatted error message: {ErrorMessage}", errorMessage);

            response.Success = false;
            response.ErrorMessage = errorMessage;
            response.ProcessingSteps = steps;

            return response;
        }
    }

    private string FormatDetailedError(Exception ex)
    {
        // Provide detailed error messages based on exception type
        return ex switch
        {
            HttpRequestException httpEx =>
                $"Network error: {httpEx.Message}\nPlease check your internet connection and try again.",

            TaskCanceledException =>
                "Request timed out. The operation took too long to complete.\nPlease try again or simplify your query.",

            UnauthorizedAccessException =>
                "API key invalid or expired.\nPlease check your OpenAI API key configuration.",

            JsonException jsonEx =>
                $"Failed to parse LLM response: {jsonEx.Message}\nThis might be a temporary issue. Please try again.",

            DatabaseConnectionException dbEx =>
                $"Database connection error: {dbEx.Message}\nPlease check your database connection settings.",

            DatabasePermissionException permEx =>
                $"Database permission error: {permEx.Message}\nPlease check your database user permissions.",

            InvalidOperationException invEx when invEx.Message.Contains("API") =>
                $"API error: {invEx.Message}\nPlease check your API key and try again.",

            _ =>
                $"Error: {ex.Message}\n\nType: {ex.GetType().Name}\n\nIf this persists, please check the logs for more details."
        };
    }

    private async Task EnsureSchemaLoadedAsync(List<string> steps, CancellationToken cancellationToken)
    {
        if (_cachedSchema != null)
        {
            steps.Add("Step 1.1: Use cached schema");
            return;
        }

        steps.Add("Step 1.1: Scan database schema");

        try
        {
            var schemaScanner = _serviceFactory.GetSchemaScanner();
            _cachedSchema = await schemaScanner.ScanAsync(cancellationToken);
        }
        catch (DatabaseConnectionException ex)
        {
            _logger.LogError(ex, "[EnhancedAgent] Cannot connect to database");
            throw;
        }
        catch (DatabasePermissionException ex)
        {
            _logger.LogError(ex, "[EnhancedAgent] Insufficient database permissions");
            throw;
        }

        // Auto-index schema
        if (!_schemaIndexed)
        {
            steps.Add("Step 1.2: Index schema into vector database");
            await TryEnsureSchemaIndexedAsync(_cachedSchema, cancellationToken);
            _schemaIndexed = true;
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

        var database = GetValue(parts, "Database", "Initial Catalog");
        if (!string.IsNullOrWhiteSpace(database))
        {
            return database;
        }

        var host = GetValue(parts, "Server", "Host", "Data Source", "DataSource");
        return host;
    }

    private RetrievedSchemaContext BuildFallbackSchema(string targetTable, DatabaseSchema fullSchema)
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

    private static string ExtractTableName(string fullName)
    {
        var parts = fullName.Split('.');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    private async Task<(SqlExecutionResult Result, List<CorrectionAttempt> Corrections)> ExecuteWithSelfCorrectionAsync(
        string initialSql,
        RetrievedSchemaContext schemaContext,
        IntentAnalysis intent,
        CancellationToken cancellationToken)
    {
        var corrections = new List<CorrectionAttempt>();
        var currentSql = initialSql;
        var attemptNumber = 0;

        var sqlExecutor = _serviceFactory.GetSqlExecutor();
        var sqlCorrector = _serviceFactory.GetSqlCorrector();

        while (attemptNumber < _agentConfig.MaxSelfCorrectionAttempts)
        {
            _logger.LogDebug("[EnhancedAgent] Executing SQL (Attempt {Attempt})", attemptNumber + 1);

            var result = await sqlExecutor.ExecuteAsync(currentSql, cancellationToken);

            if (result.Success)
            {
                if (corrections.Any())
                {
                    _logger.LogInformation(
                        "[EnhancedAgent] ✓ SQL auto-corrected successfully after {Count} attempts",
                        attemptNumber);
                }
                return (result, corrections);
            }

            _logger.LogWarning("[EnhancedAgent] SQL Error: {Error}", result.ErrorMessage);

            attemptNumber++;

            if (attemptNumber >= _agentConfig.MaxSelfCorrectionAttempts)
            {
                _logger.LogError(
                    "[EnhancedAgent] Max self-correction attempts reached ({Max})",
                    _agentConfig.MaxSelfCorrectionAttempts);
                return (result, corrections);
            }

            _logger.LogDebug("[EnhancedAgent] Attempting auto-correction...");

            var correction = await sqlCorrector.CorrectSqlAsync(
                currentSql,
                result.ErrorMessage ?? "Unknown error",
                schemaContext,
                intent,
                attemptNumber,
                cancellationToken);

            corrections.Add(correction);

            if (!correction.Success)
            {
                _logger.LogWarning("[EnhancedAgent] Unable to auto-correct SQL error");
                return (result, corrections);
            }

            if (!sqlCorrector.ShouldRetry(corrections, _agentConfig.MaxSelfCorrectionAttempts))
            {
                _logger.LogWarning("[EnhancedAgent] Stopping retry loop");
                return (result, corrections);
            }

            currentSql = correction.CorrectedSql;
            _logger.LogDebug("[EnhancedAgent] Retrying with corrected SQL...");
        }

        var finalResult = await sqlExecutor.ExecuteAsync(currentSql, cancellationToken);
        return (finalResult, corrections);
    }

    private string FormatIntelligentAnswer(
        IntentAnalysis intent,
        SqlExecutionResult result,
        List<CorrectionAttempt> corrections,
        ConversationContext context)
    {
        var answer = "";

        // Add correction info if any
        if (corrections.Any())
        {
            answer += $"ℹ️  SQL was auto-corrected {corrections.Count} time(s).\n";
        }

        // Add context-aware response
        if (context.TurnCount > 1)
        {
            answer += "📊 ";
        }

        if (result.RowCount == 0)
        {
            return answer + "No results found. Try adjusting your filters or criteria.";
        }

        answer += intent.Intent switch
        {
            QueryIntent.COUNT => $"Found {result.Rows[0].Values.First()} records.",
            QueryIntent.LIST => $"Retrieved {result.RowCount} record(s).",
            QueryIntent.SCHEMA when intent.Target.Equals("TABLES", StringComparison.OrdinalIgnoreCase) =>
                $"Database contains {result.RowCount} table(s).",
            QueryIntent.AGGREGATE => $"Analysis complete: {result.RowCount} group(s) found.",
            QueryIntent.DETAIL => $"Retrieved detailed information: {result.RowCount} record(s).",
            _ => $"Query successful: {result.RowCount} result(s)."
        };

        return answer;
    }

    public void ClearSchemaCache()
    {
        _cachedSchema = null;
        _schemaIndexed = false;
        _logger.LogInformation("[EnhancedAgent] Schema cache cleared");
    }

    private async Task<bool> TryEnsureSchemaIndexedAsync(
        DatabaseSchema schema,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[EnhancedAgent] Checking Qdrant collection...");

            var qdrantService = _serviceFactory.GetQdrantService();
            var schemaIndexer = _serviceFactory.GetSchemaIndexer();

            // Use timeout to avoid hanging when Qdrant is not available
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10 second timeout

            try
            {
                await qdrantService.EnsureCollectionAsync(cts.Token);
                var pointCount = await qdrantService.GetPointCountAsync(cts.Token);

                if (pointCount == 0)
                {
                    _logger.LogInformation("[EnhancedAgent] Indexing schema to Qdrant...");
                    await schemaIndexer.IndexSchemaAsync(schema, cts.Token);
                    _logger.LogInformation("[EnhancedAgent] ✓ Schema indexed");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("[EnhancedAgent] Qdrant not available or timeout - skipping vector indexing");
                return false; // Continue without Qdrant
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EnhancedAgent] Error indexing schema to Qdrant - continuing without it");
            return false;
        }
    }
}
