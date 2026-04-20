using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;
using Message = TextToSqlAgent.Infrastructure.Entities.Message;

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
    private readonly IIntentClassifier? _intentClassifier;
    private readonly IWritePipeline? _writePipeline;
    private readonly IDDLPipeline? _ddlPipeline;
    private readonly IForbiddenPipeline? _forbiddenPipeline;
    private readonly ISchemaCache? _schemaCache;
    private readonly IQueryResultCache? _queryResultCache;
    private readonly PipelineResponseBuilder _responseBuilder;

    // ✅ NEW-2 FIX: Make schema state static to persist across requests
    // EnhancedAgentOrchestrator is Scoped (per-request), but schema should be global
    private static DatabaseSchema? _globalCachedSchema;
    private static bool _globalSchemaIndexed = false;

    // ✅ CRIT-1 FIX: SemaphoreSlim to prevent race condition on _cachedSchema
    // Made static to work with global schema cache
    private static readonly SemaphoreSlim _globalSchemaScanLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _schemaScanLocksByConnection = new();
    private static readonly ConcurrentDictionary<string, string> _indexedSchemaHashesByConnection = new();

    // Pagination settings
    private const int DefaultPageSize = 50;  // First page size
    private const int MaxRowsBeforePagination = 100;  // Threshold for pagination

    public EnhancedAgentOrchestrator(
        IAgentServiceFactory serviceFactory,
        AgentConfig agentConfig,
        DatabaseConfig dbConfig,
        ILogger<EnhancedAgentOrchestrator> logger,
        PipelineResponseBuilder responseBuilder,
        IIntentClassifier? intentClassifier = null,
        IWritePipeline? writePipeline = null,
        IDDLPipeline? ddlPipeline = null,
        IForbiddenPipeline? forbiddenPipeline = null,
        ISchemaCache? schemaCache = null,
        IQueryResultCache? queryResultCache = null)
    {
        _serviceFactory = serviceFactory;
        _agentConfig = agentConfig;
        _dbConfig = dbConfig;
        _logger = logger;
        _responseBuilder = responseBuilder;
        _intentClassifier = intentClassifier;
        _writePipeline = writePipeline;
        _ddlPipeline = ddlPipeline;
        _forbiddenPipeline = forbiddenPipeline;
        _schemaCache = schemaCache;
        _queryResultCache = queryResultCache;

        _logger.LogInformation("[EnhancedAgent] Initialized with lazy loading (fast startup mode)");

        if (_intentClassifier != null)
        {
            _logger.LogInformation("[EnhancedAgent] Intent-based routing ENABLED (WRITE/DDL/FORBIDDEN pipelines available)");
        }

        if (_queryResultCache != null)
        {
            _logger.LogInformation("[EnhancedAgent] Query result caching ENABLED (pagination support)");
        }
    }

    /// <summary>
    /// Process query using the new modular pipeline architecture.
    /// Routes through IPipelineStage stages in sequence.
    /// Falls back to legacy ProcessQueryAsync if pipeline is not available.
    /// </summary>
    public async Task<AgentResponse> ProcessQueryWithPipelineAsync(
        Pipeline.PipelineOrchestrator pipelineOrchestrator,
        string userQuestion,
        string? connectionId = null,
        string? conversationId = null,
        List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory = null,
        IProgress<AgentStageEvent>? progress = null,
        Action<string>? sqlTokenCallback = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[EnhancedAgent] 🚀 Using modular pipeline (Phase 1 refactor)");

        DatabaseSchema? scopedSchema = null;
        if (!string.IsNullOrWhiteSpace(connectionId) && _schemaCache != null)
        {
            scopedSchema = await _schemaCache.GetAsync(connectionId, cancellationToken);
        }

        var context = new Pipeline.PipelineContext
        {
            UserQuestion = userQuestion,
            EnrichedQuestion = userQuestion, // Will be overwritten by ValidationStage
            ConversationId = conversationId,
            ConversationHistory = conversationHistory,
            Progress = progress,
            SqlTokenCallback = sqlTokenCallback,
            Schema = scopedSchema ?? _globalCachedSchema // Share scoped or fallback to global cached schema
        };

        return await pipelineOrchestrator.ExecuteAsync(context, cancellationToken);
    }

    /// <summary>
    /// Process query with full agentic capabilities (LEGACY — kept for backward compatibility).
    /// NOW WITH INTENT-BASED ROUTING: Automatically routes to WRITE/DDL/FORBIDDEN pipelines when needed.
    /// REAL PROGRESS REPORTING: Emits IProgress&lt;AgentStageEvent&gt; at each actual processing step.
    /// </summary>
    public async Task<AgentResponse> ProcessQueryAsync(
        string userQuestion,
        string? conversationId = null,
        List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory = null,
        IProgress<AgentStageEvent>? progress = null,
        Action<string>? sqlTokenCallback = null,
        IntentClassificationResult? preClassified = null, // ✅ TASK 1.2: NEW parameter to avoid double classification
        CancellationToken cancellationToken = default)
    {
        return await ProcessQueryInternalAsync(
            userQuestion,
            connectionId: null,
            conversationId,
            conversationHistory,
            persistedContext: null,
            schema: null, // ✅ PHASE-1 TASK-01: No schema injection for legacy calls
            progress,
            sqlTokenCallback,
            preClassified,
            cancellationToken);
    }

    private async Task<AgentResponse> ProcessQueryInternalAsync(
        string userQuestion,
        string? connectionId,
        string? conversationId,
        List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory,
        SerializableConversationContext? persistedContext,
        DatabaseSchema? schema, // ✅ PHASE-1 TASK-01: Add schema parameter
        IProgress<AgentStageEvent>? progress,
        Action<string>? sqlTokenCallback,
        IntentClassificationResult? preClassified,
        CancellationToken cancellationToken)
    {
        var response = new AgentResponse();
        var steps = new List<string>();

        try
        {
            _logger.LogInformation("[EnhancedAgent] 🤖 Processing query with agentic AI");

            // Log conversation context
            if (conversationHistory?.Any() == true)
            {
                _logger.LogInformation("[EnhancedAgent] 💬 Using conversation history: {Count} messages", conversationHistory.Count);
            }

            // ====================================
            // STEP -1: INTENT CLASSIFICATION (LEGACY - DISABLED)
            // ✅ SERIOUS-5 FIX: Removed duplicate intent classification
            // Intent is now classified ONCE in ProcessMessageWithIntentRoutingAsync
            // This method (ProcessQueryAsync) is called AFTER routing decision is made
            // ====================================
            // ✅ TASK 1.2: If preClassified is provided, use it instead of classifying again
            if (preClassified != null)
            {
                _logger.LogInformation(
                    "[EnhancedAgent] ✅ Using pre-classified intent: {Intent} (confidence: {Confidence:P0}, entities: [{Entities}])",
                    preClassified.Intent,
                    preClassified.Confidence,
                    string.Join(", ", preClassified.DetectedEntities ?? new List<string>()));
            }
            // NOTE: If this method is called directly (not through routing), it will skip
            // intent-based routing and process as a regular query. This is intentional
            // for backward compatibility with direct calls.

            // ====================================
            // STEP 0: NORMALIZE QUESTION
            // ====================================

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

                // ✅ CRITICAL FIX: Populate context with conversation history from database
                if (conversationHistory?.Any() == true)
                {
                    _logger.LogInformation("[EnhancedAgent] 📚 Populating context with {Count} messages from database", conversationHistory.Count);

                    // Convert database messages to conversation turns
                    var turns = new List<ConversationTurn>();
                    Message? lastUserMessage = null;

                    foreach (var msg in conversationHistory.OrderBy(m => m.CreatedAt))
                    {
                        if (msg.Role == "user")
                        {
                            lastUserMessage = msg;
                        }
                        else if (msg.Role == "assistant" && lastUserMessage != null)
                        {
                            // ✅ Extract structured context from SQL query
                            string? targetTable = null;
                            List<string> entitiesReferenced = new();
                            string? primaryEntity = null;
                            Dictionary<string, string> columns = new();
                            string? queryIntentType = null;

                            if (!string.IsNullOrEmpty(msg.SqlQuery))
                            {
                                // Use SqlContextExtractor for full context extraction
                                var (tables, primary, cols, intentType) = Core.Helpers.SqlContextExtractor.ExtractFullContext(msg.SqlQuery);

                                entitiesReferenced = tables;
                                primaryEntity = primary;
                                targetTable = primary;
                                columns = cols;
                                queryIntentType = intentType;

                                _logger.LogDebug(
                                    "[EnhancedAgent] 📦 Extracted context from history: Primary={Primary}, Entities=[{Entities}], Intent={Intent}",
                                    primaryEntity,
                                    string.Join(", ", entitiesReferenced),
                                    queryIntentType);
                            }

                            // Create a turn from user + assistant pair
                            turns.Add(new ConversationTurn
                            {
                                TurnNumber = turns.Count + 1,
                                UserQuestion = lastUserMessage.Content ?? string.Empty,
                                SystemResponse = msg.Content ?? string.Empty,
                                SqlQuery = msg.SqlQuery,
                                TargetTable = targetTable,
                                EntitiesReferenced = entitiesReferenced,
                                PrimaryEntity = primaryEntity,
                                Columns = columns,
                                QueryIntentType = queryIntentType,
                                Timestamp = msg.CreatedAt,
                                Success = msg.Success
                            });
                            lastUserMessage = null;
                        }
                    }

                    // Replace context history with database history
                    context.History = turns;

                    _logger.LogInformation("[EnhancedAgent] ✅ Context populated with {TurnCount} conversation turns", turns.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to create conversation context: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to create conversation context: {ex.Message}", ex);
            }

            ApplyPersistedContext(context, persistedContext);

            // ====================================
            // STEP 0.5: Enrich question with conversation context (BEFORE validation)
            // ====================================
            // ✅ CRITICAL: Enrich question FIRST so validator understands context
            var pronounsDetected = false;
            if (context.History.Count > 0)
            {
                var resolver = _serviceFactory.GetOrCreate<CoreferenceResolver>();
                pronounsDetected = resolver.ContainsPronouns(userQuestion);
            }

            var enrichedQuestion = conversationManager.EnrichQuestionWithContext(context, userQuestion);

            if (enrichedQuestion != userQuestion)
            {
                _logger.LogInformation(
                    "[EnhancedAgent] 🔄 Question enriched:\n  Original: '{Original}'\n  Enriched: '{Enriched}'",
                    userQuestion,
                    enrichedQuestion);

                // Log context entities if available
                if (context.History.Any())
                {
                    var lastTurn = context.History.Last();
                    if (lastTurn.EntitiesReferenced.Any())
                    {
                        _logger.LogInformation(
                            "[EnhancedAgent] 📦 Context entities: [{Entities}], Primary: {Primary}",
                            string.Join(", ", lastTurn.EntitiesReferenced),
                            lastTurn.PrimaryEntity ?? "none");

                        // ✅ Store context info for response
                        response.ContextEntities = lastTurn.EntitiesReferenced;
                        response.PrimaryEntity = lastTurn.PrimaryEntity;
                        response.PronounsResolved = pronounsDetected;
                    }
                }
            }

            // ====================================
            // STEP 0: Query Validation & Routing
            // ====================================
            steps.Add("Step 0: Validate query relevance");

            // ✅ PROGRESS: Emit validation stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.VALIDATING,
                Message = "Validating and normalizing your question...",
                Progress = 0.05
            });

            // PHASE 3 OPTIMIZATION: Validate WITHOUT schema first (fast path)
            // Only load schema if validation passes
            QueryValidationResult validation;
            try
            {
                // ✅ Use enriched question for validation
                var availableTablesForValidation = new List<string>();
                if (!string.IsNullOrWhiteSpace(connectionId) && _schemaCache != null)
                {
                    var cachedValidationSchema = await _schemaCache.GetAsync(connectionId, cancellationToken);
                    if (cachedValidationSchema != null)
                    {
                        availableTablesForValidation = cachedValidationSchema.Tables
                            .Select(t => t.TableName)
                            .ToList();
                    }
                }

                validation = await queryValidator.ValidateQueryAsync(
                    enrichedQuestion,
                    availableTablesForValidation,
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

            // ✅ PROGRESS: Emit schema retrieval stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.SCHEMA_RETRIEVAL,
                Message = "Loading database schema...",
                Progress = 0.20
            });

            DatabaseSchema? loadedSchema;
            try
            {
                // ✅ PHASE-1 TASK-01: Use injected schema if provided (skip loading)
                if (schema != null)
                {
                    _logger.LogInformation("[EnhancedAgent] ✅ Schema injected from cache, skipping database scan");
                    loadedSchema = schema;
                }
                else
                {
                    _logger.LogInformation("[EnhancedAgent] Schema not injected, loading from database");
                    loadedSchema = !string.IsNullOrWhiteSpace(connectionId)
                        ? await EnsureSchemaLoadedForConnectionAsync(connectionId, steps, cancellationToken)
                        : await EnsureLegacySchemaLoadedAsync(steps, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnhancedAgent] Failed to load schema: {Message}", ex.Message);
                throw new InvalidOperationException($"Failed to load database schema: {ex.Message}", ex);
            }

            var tableNames = loadedSchema?.Tables.Select(t => t.TableName).ToList() ?? new List<string>();

            // ====================================
            // STEP 2: Context-Aware Normalization
            // ====================================
            steps.Add("Step 2: Normalize with conversation context");

            // Question already enriched in Step 0.5
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

            // ✅ PROGRESS: Emit RAG retrieval stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.SCHEMA_RETRIEVAL,
                Message = "Finding relevant tables and relationships...",
                Progress = 0.35,
                Detail = "Using vector search to identify relevant schema"
            });

            ConfigureQdrantCollectionName(connectionId);

            var schemaRetriever = _serviceFactory.GetSchemaRetriever();
            var relevantSchema = await schemaRetriever.RetrieveAsync(
                normalized.NormalizedText,
                loadedSchema!,
                connectionId,
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

                relevantSchema = BuildFallbackSchema(intent.Target, loadedSchema!);
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

            // ✅ PROGRESS: Emit SQL generation stage (LLM call - longest step)
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.SQL_GENERATION,
                Message = "Generating SQL query with AI...",
                Progress = 0.50,
                Detail = $"Target: {intent.Target}"
            });

            var sqlGenerator = _serviceFactory.GetSqlGenerator();
            var structuredPromptContext = GetStructuredPromptContext(persistedContext);

            // ✅ Use streaming API if callback provided, otherwise use regular API
            SqlGenerationResult sqlResult;
            if (sqlTokenCallback != null)
            {
                sqlResult = await sqlGenerator.GenerateSqlWithContextStreamAsync(
                    intent,
                    relevantSchema,
                    normalized.NormalizedText,
                    conversationHistory,
                    sqlTokenCallback,  // ← Stream tokens to callback
                    structuredPromptContext,
                    cancellationToken);
            }
            else
            {
                sqlResult = await sqlGenerator.GenerateSqlWithContextAsync(
                    intent,
                    relevantSchema,
                    normalized.NormalizedText,
                    conversationHistory,
                    structuredPromptContext,
                    cancellationToken);
            }

            var sql = sqlResult.Sql;

            // ====================================
            // STEP 7: Validate SQL Safety
            // ====================================
            steps.Add("Step 7: Validate SQL safety");

            // ✅ PROGRESS: Emit SQL validation stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.SQL_VALIDATION,
                Message = "Validating SQL safety...",
                Progress = 0.65
            });

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

            // ✅ PROGRESS: Emit execution stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.EXECUTING,
                Message = "Executing SQL query...",
                Progress = 0.75
            });

            var (executionResult, corrections) = await ExecuteWithSelfCorrectionAsync(
                sql,
                relevantSchema,
                intent,
                progress,  // ← Pass progress reporter
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

            // ✅ PROGRESS: Emit building response stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.BUILDING_RESPONSE,
                Message = "Building final response...",
                Progress = 0.90
            });

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

            // ✅ PROGRESS: Emit completed stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.COMPLETED,
                Message = "Processing complete!",
                Progress = 1.0
            });

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

            // ✅ PROGRESS: Emit error stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.ERROR,
                Message = "An error occurred during processing",
                Progress = 0.0,
                Detail = errorMessage
            });

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

    private async Task<DatabaseSchema> EnsureLegacySchemaLoadedAsync(List<string> steps, CancellationToken cancellationToken)
    {
        // ✅ CRIT-1 FIX: Double-check locking pattern to prevent race condition
        // ✅ NEW-2 FIX: Use global static cache to persist across requests
        if (_globalCachedSchema != null)
        {
            steps.Add("Step 1.1: Use global cached schema");
            return _globalCachedSchema;
        }

        await _globalSchemaScanLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_globalCachedSchema != null)
            {
                steps.Add("Step 1.1: Use global cached schema (acquired after lock)");
                return _globalCachedSchema;
            }

            steps.Add("Step 1.1: Scan database schema");

            try
            {
                var schemaScanner = _serviceFactory.GetSchemaScanner();
                _globalCachedSchema = await schemaScanner.ScanAsync(cancellationToken);
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

            // Auto-index schema (only once per app lifetime)
            if (!_globalSchemaIndexed)
            {
                steps.Add("Step 1.2: Index schema into vector database");
                await TryEnsureSchemaIndexedAsync(_globalCachedSchema, cancellationToken);
                _globalSchemaIndexed = true;
                _logger.LogInformation("[EnhancedAgent] ✅ Schema indexed globally (will persist across requests)");
            }
            return _globalCachedSchema!;
        }
        finally
        {
            _globalSchemaScanLock.Release();
        }
    }

    private async Task<DatabaseSchema> EnsureSchemaLoadedForConnectionAsync(
        string connectionId,
        List<string> steps,
        CancellationToken cancellationToken)
    {
        if (_schemaCache == null)
        {
            _logger.LogWarning("[Schema] Connection-scoped schema cache unavailable, using legacy load path");
            return await EnsureLegacySchemaLoadedAsync(steps, cancellationToken);
        }

        var cachedSchema = await _schemaCache.GetAsync(connectionId, cancellationToken);
            if (cachedSchema != null)
            {
                steps.Add("Step 1.1: Use connection-scoped schema cache");
                ConfigureQdrantCollectionName(connectionId);
                await TryEnsureSchemaIndexedAsync(cachedSchema, connectionId, cancellationToken);
                return cachedSchema;
            }

        var schemaLock = _schemaScanLocksByConnection.GetOrAdd(
            connectionId,
            _ => new SemaphoreSlim(1, 1));

        await schemaLock.WaitAsync(cancellationToken);
        try
        {
            cachedSchema = await _schemaCache.GetAsync(connectionId, cancellationToken);
            if (cachedSchema != null)
            {
                steps.Add("Step 1.1: Use connection-scoped schema cache (acquired after lock)");
                ConfigureQdrantCollectionName(connectionId);
                await TryEnsureSchemaIndexedAsync(cachedSchema, connectionId, cancellationToken);
                return cachedSchema;
            }

            steps.Add("Step 1.1: Scan database schema for the active connection");

            var schemaScanner = _serviceFactory.GetSchemaScanner();
            var schema = await schemaScanner.ScanAsync(cancellationToken);
            await _schemaCache.SetAsync(connectionId, schema, cancellationToken);

            ConfigureQdrantCollectionName(connectionId);
            await TryEnsureSchemaIndexedAsync(schema, connectionId, cancellationToken);
            return schema;
        }
        finally
        {
            schemaLock.Release();
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

    private void ConfigureQdrantCollectionName(string? connectionId = null)
    {
        try
        {
            var collectionKey = ExtractDatabaseName(_dbConfig);
            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                collectionKey = connectionId;
            }

            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                _logger.LogDebug("[EnhancedAgent] No collection key available, keeping current Qdrant collection");
                return;
            }

            var qdrantService = _serviceFactory.GetQdrantService();
            qdrantService.SetCollectionName(collectionKey);
            _logger.LogDebug("[EnhancedAgent] Qdrant collection configured for key: {CollectionKey}", collectionKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EnhancedAgent] Failed to configure Qdrant collection name");
        }
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
        IProgress<AgentStageEvent>? progress,
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

            // ✅ PROGRESS: Emit correcting stage
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.CORRECTING,
                Message = $"Auto-correcting SQL (attempt {attemptNumber})...",
                Progress = 0.75 + (attemptNumber * 0.03), // Increment slightly per attempt
                Detail = result.ErrorMessage
            });

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
        _globalCachedSchema = null;
        _globalSchemaIndexed = false;
        _indexedSchemaHashesByConnection.Clear();
        _logger.LogInformation("[EnhancedAgent] Global schema cache cleared");
    }

    private Task<bool> TryEnsureSchemaIndexedAsync(
        DatabaseSchema schema,
        CancellationToken cancellationToken)
        => TryEnsureSchemaIndexedAsync(schema, null, cancellationToken);

    private async Task<bool> TryEnsureSchemaIndexedAsync(
        DatabaseSchema schema,
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[EnhancedAgent] Checking Qdrant collection...");
            ConfigureQdrantCollectionName(connectionId);

            var qdrantService = _serviceFactory.GetQdrantService();
            var schemaIndexer = _serviceFactory.GetSchemaIndexer();
            var fingerprint = CreateSimpleFingerprint(schema);
            var fingerprintCacheKey = connectionId ?? "__legacy__";
            var collectionExists = await qdrantService.CollectionExistsAsync(cancellationToken);

            if (_indexedSchemaHashesByConnection.TryGetValue(fingerprintCacheKey, out var cachedHash) &&
                string.Equals(cachedHash, fingerprint.Hash, StringComparison.Ordinal) &&
                collectionExists)
            {
                return true;
            }

            // ✅ FIX #1: Increase timeout - 163 docs need several minutes
            // Use original cancellationToken, don't set hard timeout here
            // Let embedding client manage its own timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // Enough for 163 docs

            try
            {
                await qdrantService.EnsureCollectionAsync(cts.Token);
                var schemaAlreadyIndexed = await schemaIndexer.IsSchemaIndexedAsync(fingerprint, cts.Token);

                if (!schemaAlreadyIndexed)
                {
                    _logger.LogInformation("[EnhancedAgent] Indexing schema to Qdrant...");

                    // ✅ FIX #2: Check return value
                    var result = connectionId != null
                        ? await schemaIndexer.IndexSchemaAsync(schema, fingerprint, connectionId, cts.Token)
                        : await schemaIndexer.IndexSchemaAsync(schema, fingerprint, cts.Token);

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

    // ═══════════════════════════════════════════════════════════════
    // NEW: INTENT-BASED ROUTING (PHASE 1)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Process message with intent-based routing to appropriate pipeline
    /// Routes to: QUERY (existing), WRITE (new), DDL (new), or FORBIDDEN (new)
    /// </summary>
    /// <param name="userQuestion">User's natural language question</param>
    /// <param name="connectionId">Database connection ID</param>
    /// <param name="conversationId">Optional conversation ID</param>
    /// <param name="conversationHistory">Previous messages in conversation</param>
    /// <param name="progress">Progress reporter for SSE stage updates</param>
    /// <param name="sqlTokenCallback">Callback for SQL token streaming</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>UnifiedPipelineResponse with pipeline-specific data</returns>
    public async Task<UnifiedPipelineResponse> ProcessMessageWithIntentRoutingAsync(
        string userQuestion,
        string connectionId,
        string? conversationId = null,
        List<Message>? conversationHistory = null,
        IProgress<AgentStageEvent>? progress = null,
        Action<string>? sqlTokenCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await ProcessMessageWithIntentRoutingAsync(
            userQuestion,
            connectionId,
            conversationId,
            conversationHistory,
            persistedContext: null,
            schema: null, // ✅ PHASE-1 TASK-01: No schema for backward compatibility
            progress,
            sqlTokenCallback,
            cancellationToken);
    }

    public async Task<UnifiedPipelineResponse> ProcessMessageWithIntentRoutingAsync(
        string userQuestion,
        string connectionId,
        string? conversationId,
        List<Message>? conversationHistory,
        SerializableConversationContext? persistedContext,
        DatabaseSchema? schema, // ✅ PHASE-1 TASK-01: Add schema parameter
        IProgress<AgentStageEvent>? progress,
        Action<string>? sqlTokenCallback,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("[EnhancedAgent] Processing with intent-based routing");

        // ✅ PHASE-1 TASK-01: Log schema injection status
        if (schema != null)
        {
            _logger.LogInformation(
                "[EnhancedAgent] ✅ Schema injected ({TableCount} tables), skipping scan",
                schema.Tables.Count);
        }

        // Check if intent routing is enabled
        if (_intentClassifier == null)
        {
            _logger.LogWarning("[EnhancedAgent] Intent classifier not available, falling back to QUERY pipeline");
            var queryResponse = await ProcessQueryInternalAsync(
                userQuestion,
                connectionId,
                conversationId,
                conversationHistory,
                persistedContext,
                schema, // ✅ PHASE-1 TASK-01: Pass schema
                progress,
                sqlTokenCallback,
                null,
                cancellationToken);
            return _responseBuilder.BuildQueryResponse(queryResponse, null, stopwatch);
        }

        try
        {
            // Report progress: Intent Classification
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.CLASSIFYING,
                Message = "Classifying intent...",
                Progress = 0.1,
                Timestamp = DateTime.UtcNow
            });

            // Step 1: Classify intent
            _logger.LogInformation("[EnhancedAgent] Step 1: Classifying intent");

            var conversationContext = BuildConversationContext(conversationHistory, persistedContext);

            // ✅ FIX: Use injected schema to build context (avoid redundant Redis round-trip)
            // schema is already loaded by the controller from ISchemaCache before entering this method.
            var databaseContext = schema != null
                ? BuildDatabaseContextFromSchema(schema)
                : await BuildDatabaseContextAsync(connectionId, cancellationToken);

            if (schema != null)
            {
                _logger.LogDebug("[EnhancedAgent] ✅ Using injected schema for databaseContext (skip cache read)");
            }

            var intentResult = await _intentClassifier.ClassifyAsync(
                userQuestion,
                conversationContext,
                databaseContext,
                cancellationToken);

            _logger.LogInformation(
                "[EnhancedAgent] Intent classified: {Intent} → Route: {Route} (confidence: {Confidence:P0})",
                intentResult.Intent,
                intentResult.Route,
                intentResult.Confidence);

            // Report progress: Routing decision
            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.AGENT_THINKING,
                Message = $"Routing to {intentResult.Route} pipeline...",
                Progress = 0.15,
                Detail = intentResult.Route.ToString(),
                Timestamp = DateTime.UtcNow
            });

            // Step 2: Route to appropriate pipeline
            return intentResult.Route switch
            {
                PipelineRoute.Query => await RouteToQueryPipelineAsync(
                    userQuestion, connectionId, conversationId, conversationHistory, persistedContext, schema, intentResult, stopwatch, progress, sqlTokenCallback, cancellationToken),

                PipelineRoute.Write => await RouteToWritePipelineAsync(
                    userQuestion, connectionId, conversationId, intentResult, stopwatch, progress, schema, cancellationToken),

                PipelineRoute.Ddl => await RouteToDDLPipelineAsync(
                    userQuestion, connectionId, conversationId, intentResult, stopwatch, progress, schema, cancellationToken),

                PipelineRoute.Forbidden => await RoutToForbiddenPipeline(
                    userQuestion, intentResult, stopwatch, cancellationToken),

                PipelineRoute.Reject => CreateRejectionResponse(intentResult, stopwatch),

                _ => throw new NotSupportedException($"Unknown pipeline route: {intentResult.Route}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EnhancedAgent] Error in intent-based routing");
            return _responseBuilder.BuildErrorResponse(ex, PipelineType.Query);
        }
    }

    private string BuildConversationContext(List<Message>? conversationHistory)
        => BuildConversationContext(conversationHistory, null);

    private string BuildConversationContext(
        List<Message>? conversationHistory,
        SerializableConversationContext? persistedContext)
    {
        var context = new StringBuilder();

        var structuredContext = GetStructuredPromptContext(persistedContext);
        if (!string.IsNullOrWhiteSpace(structuredContext))
        {
            context.AppendLine("Structured conversation memory:");
            context.AppendLine(structuredContext);
            context.AppendLine();
        }

        if (conversationHistory?.Any() == true)
        {
            context.AppendLine("Recent conversation:");

            foreach (var msg in conversationHistory.TakeLast(6))
            {
                context.AppendLine($"{msg.Role}: {msg.Content}");

                if (msg.Role == "assistant" && !string.IsNullOrWhiteSpace(msg.SqlQuery))
                {
                    context.AppendLine($"assistant_sql: {msg.SqlQuery}");
                }

            }
        }

        return context.ToString().Trim();
    }

    private static string? GetStructuredPromptContext(SerializableConversationContext? persistedContext)
    {
        if (persistedContext == null)
        {
            return null;
        }

        var structuredContext = persistedContext.ToSystemPromptContext();
        return string.Equals(structuredContext, "No previous conversation.", StringComparison.Ordinal)
            ? null
            : structuredContext;
    }

    private void ApplyPersistedContext(
        ConversationContext context,
        SerializableConversationContext? persistedContext)
    {
        if (persistedContext == null)
        {
            return;
        }

        foreach (var table in persistedContext.MentionedTables)
        {
            if (!context.RecentTables.Contains(table))
            {
                context.RecentTables.Add(table);
            }
        }

        if (!string.IsNullOrWhiteSpace(persistedContext.LastSql) &&
            string.IsNullOrWhiteSpace(context.LastSqlQuery))
        {
            context.LastSqlQuery = persistedContext.LastSql;
        }

        if (!string.IsNullOrWhiteSpace(persistedContext.LastResultSummary) &&
            string.IsNullOrWhiteSpace(context.LastResultSummary))
        {
            context.LastResultSummary = persistedContext.LastResultSummary;
        }
    }

    /// <summary>
    /// Ensure schema is loaded for the connection. Auto-scan if cache miss.
    /// ✅ SERIOUS-6 FIX: Proper null handling for _schemaCache
    /// </summary>
    private async Task<DatabaseSchema?> EnsureSchemaLoadedAsync(string connectionId, CancellationToken ct)
    {
        try
        {
            // ✅ FIX: Check if cache is available before using
            if (_schemaCache == null)
            {
                _logger.LogWarning("[Schema] Schema cache not available for connection {ConnectionId}", connectionId);
                return null;
            }

            // Try cache first
            var schema = await _schemaCache.GetAsync(connectionId, ct);
            if (schema != null)
            {
                _logger.LogDebug("[Schema] Cache hit for connection {ConnectionId}", connectionId);
                return schema;
            }

            // Cache miss - scan now
            _logger.LogWarning("[Schema] Cache miss for {ConnectionId}, initiating auto-scan", connectionId);

            var schemaScanner = _serviceFactory.GetSchemaScanner();
            schema = await schemaScanner.ScanAsync(ct);

            if (schema == null)
            {
                _logger.LogError("[Schema] Failed to scan schema for {ConnectionId}", connectionId);
                return null;
            }

            // Save to cache
            await _schemaCache.SetAsync(connectionId, schema, ct);
            _logger.LogInformation("[Schema] Auto-scanned and cached {TableCount} tables for {ConnectionId}",
                schema.Tables.Count, connectionId);

            return schema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Schema] Error loading schema for {ConnectionId}", connectionId);
            return null;
        }
    }

    /// <summary>
    /// Build database context from a pre-loaded schema (no async I/O, zero latency).
    /// ✅ FIX: Avoids redundant Redis/cache read when schema is already injected.
    /// </summary>
    private string BuildDatabaseContextFromSchema(DatabaseSchema schema)
    {
        try
        {
            var context = new System.Text.StringBuilder();
            context.AppendLine($"Total Tables: {schema.Tables.Count}");
            context.AppendLine();
            context.AppendLine("Available Tables:");

            foreach (var table in schema.Tables.Take(15))
            {
                context.AppendLine($"  • {table.TableName} ({table.Columns.Count} columns)");
                var keyColumns = table.Columns.Take(5).Select(c => c.ColumnName);
                context.AppendLine($"    Columns: {string.Join(", ", keyColumns)}");
            }

            if (schema.Tables.Count > 15)
            {
                context.AppendLine($"  ... and {schema.Tables.Count - 15} more tables");
            }

            return context.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Schema] Error building database context from injected schema");
            return string.Empty;
        }
    }

    private async Task<string> BuildDatabaseContextAsync(string connectionId, CancellationToken ct)
    {
        try
        {
            var schema = await EnsureSchemaLoadedAsync(connectionId, ct);

            if (schema == null)
            {
                _logger.LogError("[Schema] Cannot build database context - schema is null for {ConnectionId}", connectionId);
                return string.Empty;
            }

            return BuildDatabaseContextFromSchema(schema);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Schema] Error building database context");
            return string.Empty;
        }
    }

    private async Task<UnifiedPipelineResponse> RouteToQueryPipelineAsync(
        string userQuestion,
        string connectionId,
        string? conversationId,
        List<Message>? conversationHistory,
        SerializableConversationContext? persistedContext,
        DatabaseSchema? schema, // ✅ PHASE-1 TASK-01: Add schema parameter
        IntentClassificationResult intentResult,
        System.Diagnostics.Stopwatch stopwatch,
        IProgress<AgentStageEvent>? progress,
        Action<string>? sqlTokenCallback,
        CancellationToken ct)
    {
        _logger.LogInformation("[EnhancedAgent] → Routing to QUERY pipeline");

        // Report progress: Starting query pipeline
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.SCHEMA_RETRIEVAL,
            Message = "Retrieving database schema...",
            Progress = 0.2,
            Timestamp = DateTime.UtcNow
        });

        // ✅ TASK 1.2: Pass intentResult to avoid double classification
        var queryResponse = await ProcessQueryInternalAsync(
            userQuestion,
            connectionId,
            conversationId,
            conversationHistory,
            persistedContext,
            schema, // ✅ PHASE-1 TASK-01: Pass schema
            progress,
            sqlTokenCallback,
            intentResult, // ← Pass pre-classified intent
            ct);

        // ✅ Check if result needs pagination
        var rowCount = queryResponse.QueryResult?.RowCount ?? 0;
        string? resultId = null;
        bool hasMore = false;

        if (rowCount > MaxRowsBeforePagination && _queryResultCache != null)
        {
            _logger.LogInformation(
                "[EnhancedAgent] Large result set ({RowCount} rows), enabling pagination",
                rowCount);

            // Cache full result
            resultId = await _queryResultCache.CacheResultAsync(
                queryResponse.QueryResult!,
                _dbConfig.ConnectionString ?? string.Empty,
                conversationId,
                TimeSpan.FromMinutes(10),
                ct);

            // Return only first page
            var firstPage = queryResponse.QueryResult!.Rows
                .Take(DefaultPageSize)
                .ToList();

            var originalResult = queryResponse.QueryResult;
            queryResponse.QueryResult = new SqlExecutionResult
            {
                Rows = firstPage,
                Columns = originalResult.Columns,
                Success = originalResult.Success,
                ExecutionTimeMs = originalResult.ExecutionTimeMs,
                RowsAffected = originalResult.RowsAffected,
                ErrorMessage = originalResult.ErrorMessage,
                ErrorDetails = originalResult.ErrorDetails
            };

            hasMore = rowCount > DefaultPageSize;

            _logger.LogInformation(
                "[EnhancedAgent] Returning first page ({PageSize} rows), cached as {ResultId}",
                DefaultPageSize, resultId);
        }

        return _responseBuilder.BuildQueryResponse(
            queryResponse,
            intentResult,
            stopwatch,
            resultId,
            hasMore);
    }

    private async Task<UnifiedPipelineResponse> RouteToWritePipelineAsync(
        string userQuestion,
        string connectionId,
        string? conversationId,
        IntentClassificationResult intentResult,
        System.Diagnostics.Stopwatch stopwatch,
        IProgress<AgentStageEvent>? progress,
        DatabaseSchema? schema, // ✅ ADD schema parameter
        CancellationToken ct)
    {
        _logger.LogInformation("[EnhancedAgent] → Routing to WRITE pipeline");

        // Report progress: Starting WRITE pipeline
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.AGENT_THINKING,
            Message = "Generating INSERT/UPDATE preview...",
            Progress = 0.2,
            Timestamp = DateTime.UtcNow
        });

        if (_writePipeline == null)
        {
            _logger.LogWarning("[EnhancedAgent] WRITE pipeline not available");
            return _responseBuilder.BuildErrorResponse(
                new InvalidOperationException("WRITE pipeline not configured"),
                PipelineType.Write,
                intentResult);
        }

        var request = new WriteOperationRequest
        {
            Question = userQuestion,
            ConnectionId = connectionId,
            ConversationId = conversationId,
            IsConfirmed = false, // Always require confirmation
            PreResolvedEntities = intentResult.DetectedEntities, // FIX 1: Pass entities from IntentClassifier
            Schema = schema // ✅ OPTIMIZATION: Inject schema to avoid Redis round-trip
        };

        // Report progress: Analyzing query
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.SCHEMA_RETRIEVAL,
            Message = "Identifying target table...",
            Progress = 0.4,
            Timestamp = DateTime.UtcNow
        });

        var preview = await _writePipeline.GeneratePreviewAsync(request, ct);

        // Report progress: Preview generated
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.BUILDING_RESPONSE,
            Message = "Preview generated - awaiting confirmation",
            Progress = 0.9,
            Timestamp = DateTime.UtcNow
        });

        return _responseBuilder.BuildWritePreviewResponse(preview, intentResult, stopwatch);
    }

    private async Task<UnifiedPipelineResponse> RouteToDDLPipelineAsync(
        string userQuestion,
        string connectionId,
        string? conversationId,
        IntentClassificationResult intentResult,
        System.Diagnostics.Stopwatch stopwatch,
        IProgress<AgentStageEvent>? progress,
        DatabaseSchema? schema, // ✅ ADD schema parameter
        CancellationToken ct)
    {
        _logger.LogInformation("[EnhancedAgent] → Routing to DDL pipeline");

        // Report progress: Starting DDL pipeline
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.AGENT_THINKING,
            Message = "Generating DDL preview...",
            Progress = 0.2,
            Timestamp = DateTime.UtcNow
        });

        if (_ddlPipeline == null)
        {
            _logger.LogWarning("[EnhancedAgent] DDL pipeline not available");
            return _responseBuilder.BuildErrorResponse(
                new InvalidOperationException("DDL pipeline not configured"),
                PipelineType.Ddl,
                intentResult);
        }

        var request = new DDLOperationRequest
        {
            Question = userQuestion,
            ConnectionId = connectionId,
            ConversationId = conversationId,
            IsConfirmed = false, // Always require confirmation
            Schema = schema // ✅ OPTIMIZATION: Inject schema to avoid Redis round-trip
        };

        // Report progress: Analyzing DDL operation
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.SCHEMA_RETRIEVAL,
            Message = "Analyzing schema changes...",
            Progress = 0.4,
            Timestamp = DateTime.UtcNow
        });

        var preview = await _ddlPipeline.GeneratePreviewAsync(request, ct);

        // Report progress: Preview generated
        progress?.Report(new AgentStageEvent
        {
            Stage = AgentStage.BUILDING_RESPONSE,
            Message = "DDL preview generated - awaiting confirmation",
            Progress = 0.9,
            Timestamp = DateTime.UtcNow
        });

        return _responseBuilder.BuildDdlPreviewResponse(preview, intentResult, stopwatch);
    }

    private async Task<UnifiedPipelineResponse> RoutToForbiddenPipeline(
        string userQuestion,
        IntentClassificationResult intentResult,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[EnhancedAgent] → Routing to FORBIDDEN pipeline (BLOCKED)");

        if (_forbiddenPipeline == null)
        {
            // Fallback if pipeline not configured
            var fallbackResult = new ForbiddenOperationResult
            {
                IsBlocked = true,
                OriginalQuestion = userQuestion,
                RejectionReason = intentResult.ForbiddenReason ?? "This operation is not allowed",
                DetectedPatterns = intentResult.MatchedKeywords,
                SafeAlternatives = intentResult.SafeAlternatives,
                UserFacingMessage = "This operation is not allowed",
                IntentClassification = intentResult
            };

            return _responseBuilder.BuildForbiddenResponse(fallbackResult, intentResult, stopwatch);
        }

        var result = await _forbiddenPipeline.RejectAsync(
            userQuestion,
            intentResult,
            cancellationToken);

        return _responseBuilder.BuildForbiddenResponse(result, intentResult, stopwatch);
    }

    private UnifiedPipelineResponse CreateRejectionResponse(
        IntentClassificationResult intentResult,
        System.Diagnostics.Stopwatch stopwatch)
    {
        // Detect language from normalized query
        var isVietnamese = !string.IsNullOrEmpty(intentResult.NormalizedQuery) &&
            intentResult.NormalizedQuery.Any(c =>
                "àáảãạăắằẳẵặâấầẩẫậèéẻẽẹêếềểễệìíỉĩịòóỏõọôốồổỗộơớờởỡợùúủũụưứừửữựỳýỷỹỵđ".Contains(c));

        var message = (intentResult.Intent, isVietnamese) switch
        {
            (IntentCategory.OffTopic, false) =>
                "I'm a database assistant. I can only help with database-related questions.",
            (IntentCategory.OffTopic, true) =>
                "Tôi là trợ lý database. Tôi chỉ có thể giúp các câu hỏi liên quan đến cơ sở dữ liệu.",
            (IntentCategory.Unknown, false) =>
                "I couldn't understand your request. Please be more specific about what you want to do with the database.",
            (IntentCategory.Unknown, true) =>
                "Tôi không hiểu yêu cầu của bạn. Vui lòng nói rõ hơn về việc bạn muốn làm với database.",
            _ =>
                isVietnamese ? "Tôi không thể xử lý yêu cầu này." : "I cannot process this request."
        };

        return _responseBuilder.BuildRejectionResponse(intentResult, message, stopwatch);
    }
}
