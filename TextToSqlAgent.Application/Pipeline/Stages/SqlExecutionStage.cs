using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage 5: SQL Execution — executes the generated SQL against the database
/// with self-correction loop (retry on error with LLM-based correction).
/// </summary>
public class SqlExecutionStage : IPipelineStage
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly AgentConfig _agentConfig;
    private readonly ILogger<SqlExecutionStage> _logger;

    public string StageName => "SQL Execution";
    public AgentStage Stage => AgentStage.EXECUTING;
    public double ProgressStart => 0.70;

    public SqlExecutionStage(
        IAgentServiceFactory serviceFactory,
        AgentConfig agentConfig,
        ILogger<SqlExecutionStage> logger)
    {
        _serviceFactory = serviceFactory;
        _agentConfig = agentConfig;
        _logger = logger;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        context.Steps.Add("Execute SQL with self-correction");

        var (executionResult, corrections) = await ExecuteWithSelfCorrectionAsync(
            context.GeneratedSql!,
            context.SchemaContext!,
            context.Intent!,
            context.Progress,
            ct);

        context.ExecutionResult = executionResult;
        context.Corrections = corrections;
        context.Response.CorrectionHistory = corrections;
        context.Response.WasCorrected = corrections.Any();
        context.Response.CorrectionAttempts = corrections.Count;

        if (!executionResult.Success)
        {
            context.Response.Success = false;
            context.Response.ErrorMessage = executionResult.ErrorMessage;
            context.Response.SqlGenerated = context.GeneratedSql;
            context.Response.QueryResult = executionResult;

            if (context.ConversationCtx != null)
            {
                var cm = _serviceFactory.GetConversationManager();
                cm.AddTurn(context.ConversationCtx, context.UserQuestion,
                    $"Error: {executionResult.ErrorMessage}",
                    sqlQuery: context.GeneratedSql,
                    intent: context.Intent?.Intent,
                    targetTable: context.Intent?.Target,
                    success: false);
            }

            return StageResult.Stop("SQL execution failed after correction attempts");
        }

        return StageResult.Continue();
    }

    private async Task<(SqlExecutionResult Result, List<CorrectionAttempt> Corrections)>
        ExecuteWithSelfCorrectionAsync(
            string initialSql,
            RetrievedSchemaContext schemaContext,
            IntentAnalysis intent,
            IProgress<AgentStageEvent>? progress,
            CancellationToken ct)
    {
        var corrections = new List<CorrectionAttempt>();
        var currentSql = initialSql;
        var attemptNumber = 0;

        var sqlExecutor = _serviceFactory.GetSqlExecutor();
        var sqlCorrector = _serviceFactory.GetSqlCorrector();

        while (attemptNumber < _agentConfig.MaxSelfCorrectionAttempts)
        {
            _logger.LogDebug("[SqlExecution] Executing SQL (Attempt {Attempt})", attemptNumber + 1);

            var result = await sqlExecutor.ExecuteAsync(currentSql, ct);

            if (result.Success)
            {
                if (corrections.Any())
                {
                    _logger.LogInformation(
                        "[SqlExecution] ✓ SQL auto-corrected successfully after {Count} attempts",
                        attemptNumber);
                }
                return (result, corrections);
            }

            _logger.LogWarning("[SqlExecution] SQL Error: {Error}", result.ErrorMessage);
            attemptNumber++;

            if (attemptNumber >= _agentConfig.MaxSelfCorrectionAttempts)
            {
                _logger.LogError("[SqlExecution] Max correction attempts reached ({Max})",
                    _agentConfig.MaxSelfCorrectionAttempts);
                return (result, corrections);
            }

            _logger.LogDebug("[SqlExecution] Attempting auto-correction...");

            progress?.Report(new AgentStageEvent
            {
                Stage = AgentStage.CORRECTING,
                Message = $"Auto-correcting SQL (attempt {attemptNumber})...",
                Progress = 0.75 + (attemptNumber * 0.03),
                Detail = result.ErrorMessage
            });

            var correction = await sqlCorrector.CorrectSqlAsync(
                currentSql,
                result.ErrorMessage ?? "Unknown error",
                schemaContext,
                intent,
                attemptNumber,
                ct);

            corrections.Add(correction);

            if (!correction.Success)
            {
                _logger.LogWarning("[SqlExecution] Unable to auto-correct SQL error");
                return (result, corrections);
            }

            if (!sqlCorrector.ShouldRetry(corrections, _agentConfig.MaxSelfCorrectionAttempts))
            {
                _logger.LogWarning("[SqlExecution] Stopping retry loop");
                return (result, corrections);
            }

            currentSql = correction.CorrectedSql;
            _logger.LogDebug("[SqlExecution] Retrying with corrected SQL...");
        }

        var finalResult = await sqlExecutor.ExecuteAsync(currentSql, ct);
        return (finalResult, corrections);
    }
}
