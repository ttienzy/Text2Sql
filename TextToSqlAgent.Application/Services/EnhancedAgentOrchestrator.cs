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
                var clarification = validation.ClarificationQuestion ?? "Please clarify your question.";

                response.Success = false;
                response.Answer = clarification;
                response.ErrorMessage = clarification; // ✅ Set ErrorMessage để hiển thị đúng
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
            IntentAnalysis intent;
            if (relevantSchema.RelevantTables.Count == 0)
            {
                _logger.LogWarning("[EnhancedAgent] RAG found 0 results, using fallback");

                var intentPlugin = _serviceFactory.GetIntentAnalyzer();
                // Single intent analysis call (reuse the result)
                intent = await intentPlugin.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    tableNames,
                    cancellationToken);

                relevantSchema = BuildFallbackSchema(intent.Target, _cachedSchema);
            }
            else
            {
                // ====================================
                // STEP 5: Intent Analysis
                // ====================================
                steps.Add("Step 5: Analyze intent");

                var intentAnalyzer = _serviceFactory.GetIntentAnalyzer();
                intent = await intentAnalyzer.AnalyzeIntentAsync(
                    normalized.NormalizedText,
                    relevantSchema.RelevantTables.Select(t => t.TableName).ToList(),
                    cancellationToken);
            }

            if (intent.NeedsClarification)
            {
                var clarification = intent.ClarificationQuestion ?? "Question is unclear.";

                response.Success = false;
                response.Answer = clarification;
                response.ErrorMessage = clarification; // ✅ Set ErrorMessage để hiển thị đúng
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

            // ✅ Get SQL + suggestions in one API call
            var sqlResult = await sqlGenerator.GenerateSqlWithContextAsync(
                intent,
                relevantSchema,
                normalized.NormalizedText,  // Pass original question to LLM
                cancellationToken);

            var sql = sqlResult.Sql;

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

            // Apply EnsureLimit on the final executed SQL (after correction)
            var finalSql = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            finalSql = sqlGenerator.EnsureLimit(finalSql);

            var answer = await FormatIntelligentAnswerAsync(
                userQuestion,
                finalSql,
                intent,
                executionResult,
                corrections,
                context,
                cancellationToken);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = finalSql;
            response.QueryResult = executionResult;
            response.ProcessingSteps = steps;

            // ====================================
            // STEP 11: Generate Contextual Suggestions (NEW!)
            // ====================================
            steps.Add("Step 11: Generate contextual suggestions based on results");

            List<string> suggestions;
            try
            {
                // ✅ Generate suggestions based on ACTUAL RESULTS
                suggestions = await sqlGenerator.GenerateContextualSuggestionsAsync(
                    userQuestion,
                    finalSql,
                    executionResult,
                    intent,
                    cancellationToken);

                _logger.LogInformation(
                    "[EnhancedAgent] Generated {Count} contextual suggestions: [{Suggestions}]",
                    suggestions.Count,
                    string.Join(", ", suggestions.Take(3).Select(s => $"\"{s}\"")));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EnhancedAgent] Failed to generate contextual suggestions, using fallback");
                suggestions = new List<string>();
            }

            // ✅ Fallback to rule-based if contextual generation failed
            if (suggestions.Count < 3)
            {
                _logger.LogDebug("[EnhancedAgent] Contextual suggestions insufficient ({Count}), adding rule-based", suggestions.Count);

                var ruleBasedService = new RuleBasedSuggestionService();
                var ruleBased = ruleBasedService.Generate(intent.Intent, intent.Target, userQuestion);

                // Keep contextual suggestions (if any), add rule-based to reach 3 total
                var combined = suggestions
                    .Concat(ruleBased)
                    .Distinct()
                    .Take(3)
                    .ToList();

                suggestions = combined;
                _logger.LogDebug("[EnhancedAgent] Combined suggestions: {Count} total", suggestions.Count);
            }

            response.SuggestedQueries = suggestions;

            // ✅ Log final suggestion count
            _logger.LogInformation("[EnhancedAgent] Final response has {Count} suggestions", response.SuggestedQueries.Count);

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

        // ✅ FIX: Set correct collection name BEFORE indexing
        try
        {
            var dbName = ExtractDatabaseName(_dbConfig);
            if (!string.IsNullOrEmpty(dbName))
            {
                var qdrantService = _serviceFactory.GetQdrantService();
                qdrantService.SetCollectionName(dbName);
                _logger.LogInformation("[EnhancedAgent] Collection name set to: {Name}", dbName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EnhancedAgent] Cannot set collection name, using default");
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

    private async Task<string> FormatIntelligentAnswerAsync(
        string originalQuestion,
        string sqlQuery,
        IntentAnalysis intent,
        SqlExecutionResult result,
        List<CorrectionAttempt> corrections,
        ConversationContext context,
        CancellationToken cancellationToken = default)
    {
        var answer = "";

        // Add correction info if any
        if (corrections.Any())
        {
            answer += $"ℹ️  SQL đã được tự động sửa {corrections.Count} lần.\n";
        }

        // Add context-aware response
        if (context.TurnCount > 1)
        {
            answer += "📊 ";
        }

        if (result.RowCount == 0)
        {
            return answer + "Không tìm thấy kết quả nào. Hãy thử điều chỉnh bộ lọc hoặc tiêu chí tìm kiếm.";
        }

        try
        {
            // Use intelligent response plugin for better answers
            var responsePlugin = _serviceFactory.GetOrCreate<IntelligentResponsePlugin>();
            var intelligentResponse = await responsePlugin.GenerateResponseAsync(
                originalQuestion,
                sqlQuery,
                result,
                intent,
                cancellationToken);

            answer += intelligentResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EnhancedAgent] Failed to generate intelligent response, using fallback");

            // Fallback to simple format
            answer += intent.Intent switch
            {
                QueryIntent.COUNT => FormatCountAnswerSimple(result),
                QueryIntent.LIST => FormatListAnswerSimple(result),
                QueryIntent.SCHEMA when intent.Target.Equals("TABLES", StringComparison.OrdinalIgnoreCase) =>
                    FormatSchemaAnswerSimple(result),
                QueryIntent.AGGREGATE or QueryIntent.SUM or QueryIntent.AVG or QueryIntent.GROUP_BY =>
                    FormatAggregateAnswerSimple(result),
                QueryIntent.TOP_N => FormatTopNAnswerSimple(result),
                QueryIntent.DETAIL => FormatDetailAnswerSimple(result),
                QueryIntent.RANKING => FormatRankingAnswerSimple(result),
                QueryIntent.COMPARISON => FormatComparisonAnswerSimple(result),
                _ => FormatGenericAnswerSimple(result)
            };
        }

        return answer;
    }

    private static string FormatCountAnswer(SqlExecutionResult result)
    {
        var firstRow = result.Rows[0];
        var countValue = firstRow.Values.First();
        return $"Found {countValue} records.";
    }

    private static string FormatListAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Retrieved {result.RowCount} record(s).");

        var previewCount = Math.Min(result.RowCount, 3);
        if (result.Columns.Count > 0 && previewCount > 0)
        {
            sb.AppendLine("Preview:");
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
                    .Select(c => $"{c}: {FormatVal(row[c])}")
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
        sb.AppendLine($"Database contains {result.RowCount} table(s):");
        foreach (var row in result.Rows.Take(20))
        {
            var tableName = row.Values.FirstOrDefault()?.ToString() ?? "Unknown";
            sb.AppendLine($"  • {tableName}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatAggregateAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        if (result.RowCount == 1)
        {
            var row = result.Rows[0];
            var parts = result.Columns
                .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                .Select(c => $"{c}: {FormatVal(row[c])}")
                .ToList();
            sb.AppendLine(string.Join(" | ", parts));
        }
        else
        {
            sb.AppendLine($"Analysis complete: {result.RowCount} group(s) found.");
            var previewCount = Math.Min(result.RowCount, 5);
            for (int i = 0; i < previewCount; i++)
            {
                var row = result.Rows[i];
                var vals = result.Columns
                    .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                    .Select(c => $"{c}: {FormatVal(row[c])}")
                    .ToList();
                sb.AppendLine($"  • {string.Join(" | ", vals)}");
            }
            if (result.RowCount > previewCount)
                sb.AppendLine($"  ... and {result.RowCount - previewCount} more.");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatTopNAnswer(SqlExecutionResult result)
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
                .Select(c => $"{FormatVal(row[c])}")
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
                    sb.AppendLine($"  {col}: {FormatVal(row[col])}");
                }
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatRankingAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Ranking ({result.RowCount} entries):");
        for (int i = 0; i < Math.Min(result.RowCount, 10); i++)
        {
            var row = result.Rows[i];
            var vals = result.Columns
                .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                .Take(4)
                .Select(c => $"{FormatVal(row[c])}")
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
                .Select(c => $"{c}: {FormatVal(row[c])}")
                .ToList();
            sb.AppendLine($"  • {string.Join(" | ", vals)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatGenericAnswer(SqlExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Query successful: {result.RowCount} result(s).");
        var previewCount = Math.Min(result.RowCount, 3);
        if (previewCount > 0 && result.Columns.Count > 0)
        {
            var displayCols = result.Columns.Take(5).ToList();
            for (int i = 0; i < previewCount; i++)
            {
                var row = result.Rows[i];
                var vals = displayCols
                    .Where(c => row.ContainsKey(c) && row[c] != null && row[c] != DBNull.Value)
                    .Select(c => $"{FormatVal(row[c])}")
                    .ToList();
                sb.AppendLine($"  • {string.Join(" | ", vals)}");
            }
            if (result.RowCount > previewCount)
                sb.AppendLine($"  ... and {result.RowCount - previewCount} more.");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatVal(object value)
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
    private static string FormatCountAnswerSimple(SqlExecutionResult result)
    {
        var firstRow = result.Rows[0];
        var countValue = firstRow.Values.First();
        return $"Tìm thấy {countValue} bản ghi trong hệ thống.";
    }

    private static string FormatListAnswerSimple(SqlExecutionResult result)
    {
        return $"Truy xuất thành công {result.RowCount} bản ghi từ cơ sở dữ liệu.";
    }

    private static string FormatSchemaAnswerSimple(SqlExecutionResult result)
    {
        return $"Cơ sở dữ liệu chứa {result.RowCount} bảng.";
    }

    private static string FormatAggregateAnswerSimple(SqlExecutionResult result)
    {
        return $"Phân tích dữ liệu hoàn tất với {result.RowCount} kết quả tổng hợp.";
    }

    private static string FormatTopNAnswerSimple(SqlExecutionResult result)
    {
        return $"Đã xác định được {result.RowCount} mục hàng đầu theo tiêu chí yêu cầu.";
    }

    private static string FormatDetailAnswerSimple(SqlExecutionResult result)
    {
        return $"Thông tin chi tiết: {result.RowCount} bản ghi.";
    }

    private static string FormatRankingAnswerSimple(SqlExecutionResult result)
    {
        return $"Xếp hạng hoàn tất với {result.RowCount} mục.";
    }

    private static string FormatComparisonAnswerSimple(SqlExecutionResult result)
    {
        return $"So sánh hoàn tất với {result.RowCount} kết quả.";
    }

    private static string FormatGenericAnswerSimple(SqlExecutionResult result)
    {
        return $"Truy vấn thành công với {result.RowCount} kết quả.";
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

            // ✅ FIX #1: Increase timeout - 163 docs need several minutes
            // Use original cancellationToken, don't set hard timeout here
            // Let embedding client manage its own timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // Enough for 163 docs

            try
            {
                await qdrantService.EnsureCollectionAsync(cts.Token);
                var pointCount = await qdrantService.GetPointCountAsync(cts.Token);

                if (pointCount == 0)
                {
                    _logger.LogInformation("[EnhancedAgent] Indexing schema to Qdrant...");
                    var fingerprint = CreateSimpleFingerprint(schema);

                    // ✅ FIX #2: Check return value
                    var result = await schemaIndexer.IndexSchemaAsync(schema, fingerprint, cts.Token);

                    if (!result.Success)
                    {
                        _logger.LogWarning(
                            "[EnhancedAgent] Schema indexing failed: {Error} — continuing without RAG",
                            result.ErrorMessage);
                        return false;
                    }

                    _logger.LogInformation(
                        "[EnhancedAgent] ✓ Schema indexed: {Count} points in {Duration}ms",
                        result.PointsIndexed,
                        result.IndexingDuration.TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("[EnhancedAgent] Qdrant timeout - skipping vector indexing");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EnhancedAgent] Error indexing schema - continuing without it");
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
