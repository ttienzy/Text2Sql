namespace TextToSqlAgent.Application.Pipelines;

using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;

/// <summary>
/// Thin orchestrator - delegates to ports, no business logic
/// Phase 2: Clean pipeline architecture
/// </summary>
public class QueryPipeline
{
    private readonly IQueryValidator _validator;
    private readonly IIntentAnalyzer _intentAnalyzer;
    private readonly ISchemaProvider _schemaProvider;
    private readonly ISchemaRetriever _schemaRetriever;
    private readonly ISqlGenerator _sqlGenerator;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ISqlCorrector _sqlCorrector;
    private readonly IResultFormatter _resultFormatter;
    private readonly IConversationStore _conversationStore;
    private readonly ILogger<QueryPipeline> _logger;
    private readonly int _maxCorrectionAttempts;

    public QueryPipeline(
        IQueryValidator validator,
        IIntentAnalyzer intentAnalyzer,
        ISchemaProvider schemaProvider,
        ISchemaRetriever schemaRetriever,
        ISqlGenerator sqlGenerator,
        ISqlExecutor sqlExecutor,
        ISqlCorrector sqlCorrector,
        IResultFormatter resultFormatter,
        IConversationStore conversationStore,
        ILogger<QueryPipeline> logger,
        int maxCorrectionAttempts = 3)
    {
        _validator = validator;
        _intentAnalyzer = intentAnalyzer;
        _schemaProvider = schemaProvider;
        _schemaRetriever = schemaRetriever;
        _sqlGenerator = sqlGenerator;
        _sqlExecutor = sqlExecutor;
        _sqlCorrector = sqlCorrector;
        _resultFormatter = resultFormatter;
        _conversationStore = conversationStore;
        _logger = logger;
        _maxCorrectionAttempts = maxCorrectionAttempts;
    }

    public async Task<QueryResponse> ExecuteAsync(
        QueryRequest request,
        CancellationToken ct = default)
    {
        var response = new QueryResponse();
        var steps = new List<string>();

        try
        {
            _logger.LogInformation("[QueryPipeline] Processing query");

            // Step 0: Get conversation context
            var context = _conversationStore.GetOrCreate(request.ConversationId);

            // Step 1: Validate query
            steps.Add("Validate query relevance");
            var validation = await _validator.ValidateAsync(request.Question, ct);

            _logger.LogInformation(
                "[QueryPipeline] Query Type: {Type}, Relevant: {Relevant}",
                validation.QueryType,
                validation.IsRelevant);

            // Handle non-database queries (FAST PATH)
            if (!validation.IsRelevant)
            {
                response.Success = true;
                response.Answer = validation.SuggestedResponse ??
                    "I'm a database assistant. Please ask a database-related question.";
                response.ProcessingSteps = steps;
                return response;
            }

            // Handle clarification needed
            if (validation.NeedsClarification)
            {
                response.Success = false;
                response.Answer = validation.ClarificationQuestion ?? "Please clarify your question.";
                response.ProcessingSteps = steps;
                return response;
            }

            // Step 2: Load schema (lazy)
            steps.Add("Load database schema");
            var schema = await _schemaProvider.GetSchemaAsync(ct);

            // Step 3: Enrich with context
            steps.Add("Enrich with conversation context");
            var enrichedQuestion = _conversationStore.EnrichQuestion(context, request.Question);

            // Step 4: RAG retrieval
            steps.Add("Retrieve relevant schema");
            var relevantSchema = await _schemaRetriever.RetrieveAsync(enrichedQuestion, schema, ct);

            _logger.LogDebug(
                "[QueryPipeline] RAG found: {Tables} tables, {Rels} relationships",
                relevantSchema.RelevantTables.Count,
                relevantSchema.RelevantRelationships.Count);

            // Step 5: Analyze intent
            steps.Add("Analyze intent");
            var intent = await _intentAnalyzer.AnalyzeAsync(
                enrichedQuestion,
                relevantSchema.RelevantTables.Select(t => t.TableName).ToList(),
                ct);

            if (intent.NeedsClarification)
            {
                response.Success = false;
                response.Answer = intent.ClarificationQuestion ?? "Question is unclear.";
                response.ProcessingSteps = steps;
                return response;
            }

            // Step 6: Generate SQL
            steps.Add("Generate SQL");
            var sql = await _sqlGenerator.GenerateAsync(intent, relevantSchema, enrichedQuestion, ct);

            // Step 7: Validate safety
            steps.Add("Validate SQL safety");
            if (!_sqlGenerator.ValidateSafety(sql))
            {
                response.Success = false;
                response.ErrorMessage = "Unsafe SQL detected - only SELECT queries are allowed";
                response.SqlGenerated = sql;
                response.ProcessingSteps = steps;
                return response;
            }

            // Step 8: Execute with self-correction
            steps.Add("Execute SQL with self-correction");
            var (result, corrections) = await ExecuteWithCorrectionAsync(
                sql, relevantSchema, intent, ct);

            if (!result.Success)
            {
                response.Success = false;
                response.ErrorMessage = result.ErrorMessage;
                response.SqlGenerated = sql;
                response.QueryResult = result;
                response.CorrectionHistory = corrections;
                response.ProcessingSteps = steps;
                return response;
            }

            // Apply EnsureLimit on the final executed SQL (after correction)
            var finalSql = corrections.Any() ? corrections.Last().CorrectedSql : sql;
            finalSql = _sqlGenerator.EnsureLimit(finalSql, request.Options?.MaxRows ?? 1000);

            // Step 9: Format answer
            steps.Add("Format intelligent answer");
            var answer = _resultFormatter.FormatAnswer(intent, result, context);

            response.Success = true;
            response.Answer = answer;
            response.SqlGenerated = finalSql;
            response.QueryResult = result;
            response.CorrectionHistory = corrections;
            response.ProcessingSteps = steps;

            // Step 10: Save to conversation
            _conversationStore.AddTurn(context, new ConversationTurn
            {
                UserQuestion = request.Question,
                SystemResponse = answer,
                SqlQuery = response.SqlGenerated,
                Intent = intent.Intent,
                TargetTable = intent.Target,
                Success = true
            });

            _logger.LogInformation("[QueryPipeline] Processing complete");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryPipeline] Pipeline execution failed");

            response.Success = false;
            response.ErrorMessage = ex.Message;
            response.ProcessingSteps = steps;
            return response;
        }
    }

    private async Task<(SqlExecutionResult, List<CorrectionAttempt>)> ExecuteWithCorrectionAsync(
        string sql,
        RetrievedSchemaContext schema,
        IntentAnalysisResult intent,
        CancellationToken ct)
    {
        var corrections = new List<CorrectionAttempt>();
        var currentSql = sql;

        for (int attempt = 0; attempt < _maxCorrectionAttempts; attempt++)
        {
            _logger.LogDebug("[QueryPipeline] Executing SQL (Attempt {Attempt})", attempt + 1);

            var result = await _sqlExecutor.ExecuteAsync(currentSql, ct);

            if (result.Success)
            {
                if (corrections.Any())
                {
                    _logger.LogInformation(
                        "[QueryPipeline] SQL auto-corrected successfully after {Count} attempts",
                        attempt);
                }
                return (result, corrections);
            }

            _logger.LogWarning("[QueryPipeline] SQL Error: {Error}", result.ErrorMessage);

            if (attempt >= _maxCorrectionAttempts - 1)
            {
                _logger.LogError(
                    "[QueryPipeline] Max self-correction attempts reached ({Max})",
                    _maxCorrectionAttempts);
                return (result, corrections);
            }

            _logger.LogDebug("[QueryPipeline] Attempting auto-correction...");

            var correction = await _sqlCorrector.CorrectAsync(
                currentSql,
                result.ErrorMessage ?? "Unknown error",
                schema,
                intent,
                attempt,
                ct);

            corrections.Add(new CorrectionAttempt
            {
                AttemptNumber = attempt + 1,
                OriginalSql = currentSql,
                CorrectedSql = correction.CorrectedSql,
                Reasoning = correction.Explanation,
                Success = correction.Success,
                Error = new SqlError
                {
                    ErrorMessage = result.ErrorMessage ?? "Unknown error"
                }
            });

            if (!correction.Success)
            {
                _logger.LogWarning("[QueryPipeline] Unable to auto-correct SQL error");
                return (result, corrections);
            }

            if (!_sqlCorrector.ShouldRetry(corrections, _maxCorrectionAttempts))
            {
                _logger.LogWarning("[QueryPipeline] Stopping retry loop");
                return (result, corrections);
            }

            currentSql = correction.CorrectedSql;
            _logger.LogDebug("[QueryPipeline] Retrying with corrected SQL...");
        }

        var finalResult = await _sqlExecutor.ExecuteAsync(currentSql, ct);
        return (finalResult, corrections);
    }
}
