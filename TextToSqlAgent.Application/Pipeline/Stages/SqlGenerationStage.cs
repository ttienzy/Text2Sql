using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage 4: SQL Generation — generates SQL from the user question using LLM,
/// then validates safety (only SELECT allowed).
/// Supports both streaming (token-by-token) and non-streaming modes.
/// </summary>
public class SqlGenerationStage : IPipelineStage
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly AgentConfig _agentConfig;
    private readonly ILogger<SqlGenerationStage> _logger;

    public string StageName => "SQL Generation";
    public AgentStage Stage => AgentStage.SQL_GENERATION;
    public double ProgressStart => 0.50;

    public SqlGenerationStage(
        IAgentServiceFactory serviceFactory,
        AgentConfig agentConfig,
        ILogger<SqlGenerationStage> logger)
    {
        _serviceFactory = serviceFactory;
        _agentConfig = agentConfig;
        _logger = logger;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        context.Steps.Add("Generate SQL with RAG context");

        var sqlGenerator = _serviceFactory.GetSqlGenerator();
        var queryText = context.NormalizedPrompt?.NormalizedText ?? context.EnrichedQuestion;

        // ── Generate SQL (streaming or non-streaming) ──
        SqlGenerationResult sqlResult;
        if (context.SqlTokenCallback != null)
        {
            sqlResult = await sqlGenerator.GenerateSqlWithContextStreamAsync(
                context.Intent!,
                context.SchemaContext!,
                queryText,
                context.ConversationHistory,
                context.SqlTokenCallback,
                structuredConversationContext: null,
                cancellationToken: ct);
        }
        else
        {
            sqlResult = await sqlGenerator.GenerateSqlWithContextAsync(
                context.Intent!,
                context.SchemaContext!,
                queryText,
                context.ConversationHistory,
                structuredConversationContext: null,
                cancellationToken: ct);
        }

        context.SqlGenerationResult = sqlResult;
        context.GeneratedSql = sqlResult.Sql;

        // ── Validate SQL Safety ──
        context.Steps.Add("Validate SQL safety");
        context.ReportProgress(AgentStage.SQL_VALIDATION, "Validating SQL safety...", 0.65);

        if (!sqlGenerator.ValidateSql(sqlResult.Sql))
        {
            context.Response.Success = false;
            context.Response.ErrorMessage = "Unsafe SQL detected - only SELECT queries are allowed";
            context.Response.SqlGenerated = sqlResult.Sql;

            if (context.ConversationCtx != null)
            {
                var cm = _serviceFactory.GetConversationManager();
                cm.AddTurn(context.ConversationCtx, context.UserQuestion,
                    context.Response.ErrorMessage, sqlQuery: sqlResult.Sql,
                    intent: context.Intent?.Intent, targetTable: context.Intent?.Target,
                    success: false);
            }

            return StageResult.Stop("Unsafe SQL rejected");
        }

        // ── Optional: Explain query before execution ──
        // ✅ PHASE-2 TASK-09: Skip explanation for simple queries (complexity < 0.5)
        var complexityScore = context.IntentClassification?.ComplexityScore ?? 1.0;
        var shouldExplain = _agentConfig.ExplainQueriesBeforeExecution && complexityScore >= 0.5;

        if (shouldExplain)
        {
            context.Steps.Add("Explain query");
            var queryExplainer = _serviceFactory.GetQueryExplainer();
            var explanation = await queryExplainer.ExplainQueryAsync(
                sqlResult.Sql, context.UserQuestion, ct);
            context.Response.QueryExplanation = explanation;

            _logger.LogInformation("[SqlGeneration] Query explained (complexity: {Score:F2})", complexityScore);
        }
        else if (_agentConfig.ExplainQueriesBeforeExecution)
        {
            _logger.LogInformation(
                "[SqlGeneration] ⚡ Skipped query explanation for simple query (complexity: {Score:F2})",
                complexityScore);
        }

        return StageResult.Continue();
    }
}
