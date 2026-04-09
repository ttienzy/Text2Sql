using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage 6: Response Formatting — builds the final response with:
/// intelligent answer, contextual suggestions, conversation history update.
/// </summary>
public class ResponseFormattingStage : IPipelineStage
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly ILogger<ResponseFormattingStage> _logger;

    public string StageName => "Response Formatting";
    public AgentStage Stage => AgentStage.BUILDING_RESPONSE;
    public double ProgressStart => 0.90;

    public ResponseFormattingStage(
        IAgentServiceFactory serviceFactory,
        ILogger<ResponseFormattingStage> logger)
    {
        _serviceFactory = serviceFactory;
        _logger = logger;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        context.Steps.Add("Format intelligent answer and generate suggestions");

        var sqlGenerator = _serviceFactory.GetSqlGenerator();

        // Apply EnsureLimit on the final executed SQL
        var finalSql = context.Corrections.Any()
            ? context.Corrections.Last().CorrectedSql
            : context.GeneratedSql!;
        finalSql = sqlGenerator.EnsureLimit(finalSql);

        // ✅ PHASE-2 TASK 2.2c: Use combined plugin to generate response + suggestions in ONE LLM call
        string answer;
        List<string> suggestions;

        try
        {
            var combinedPlugin = _serviceFactory.GetOrCreate<TextToSqlAgent.Plugins.CombinedResponsePlugin>();
            var combined = await combinedPlugin.GenerateCombinedResponseAsync(
                context.UserQuestion,
                finalSql,
                context.ExecutionResult!,
                context.Intent!,
                ct);

            // Add correction info if applicable
            answer = context.Corrections.Any()
                ? $"ℹ️  SQL was auto-corrected {context.Corrections.Count} time(s).\n{combined.IntelligentAnswer}"
                : combined.IntelligentAnswer;

            // Add conversation context indicator
            if (context.ConversationCtx != null && context.ConversationCtx.TurnCount > 1)
            {
                answer = "📊 " + answer;
            }

            suggestions = combined.Suggestions;

            _logger.LogInformation(
                "[ResponseFormatting] ⚡ Generated response + {Count} suggestions in single LLM call (TASK 2.2c)",
                suggestions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResponseFormatting] Combined plugin failed, falling back to separate calls");

            // Fallback to old approach (2 separate LLM calls)
            answer = await FormatIntelligentAnswerAsync(
                context.UserQuestion,
                finalSql,
                context.Intent!,
                context.ExecutionResult!,
                context.Corrections,
                context.ConversationCtx,
                ct);

            try
            {
                suggestions = await sqlGenerator.GenerateContextualSuggestionsAsync(
                    context.UserQuestion, finalSql, context.ExecutionResult!, context.Intent!, ct);
            }
            catch
            {
                suggestions = new List<string>();
            }
        }

        // Fallback to rule-based if not enough suggestions
        if (suggestions.Count < 3)
        {
            var ruleBasedService = new RuleBasedSuggestionService();
            var ruleBased = ruleBasedService.Generate(
                context.Intent!.Intent, context.Intent.Target, context.UserQuestion);

            suggestions = suggestions.Concat(ruleBased).Distinct().Take(3).ToList();
        }

        context.Response.Success = true;
        context.Response.Answer = answer;
        context.Response.SqlGenerated = finalSql;
        context.Response.QueryResult = context.ExecutionResult;
        context.Response.SuggestedQueries = suggestions;

        // ── Update conversation history ──
        if (context.ConversationCtx != null)
        {
            var cm = _serviceFactory.GetConversationManager();
            cm.AddTurn(context.ConversationCtx, context.UserQuestion, answer,
                sqlQuery: finalSql,
                intent: context.Intent!.Intent,
                targetTable: context.Intent.Target,
                success: true);
        }

        return StageResult.Continue();
    }

    private async Task<string> FormatIntelligentAnswerAsync(
        string originalQuestion,
        string sqlQuery,
        IntentAnalysis intent,
        SqlExecutionResult result,
        List<CorrectionAttempt> corrections,
        ConversationContext? context,
        CancellationToken ct)
    {
        var answer = "";

        if (corrections.Any())
        {
            answer += $"ℹ️  SQL was auto-corrected {corrections.Count} time(s).\n";
        }

        if (context != null && context.TurnCount > 1)
        {
            answer += "📊 ";
        }

        if (result.RowCount == 0)
        {
            return answer + "No results found. Try adjusting your filters or search criteria.";
        }

        try
        {
            var responsePlugin = _serviceFactory.GetOrCreate<TextToSqlAgent.Plugins.IntelligentResponsePlugin>();
            var intelligentResponse = await responsePlugin.GenerateResponseAsync(
                originalQuestion, sqlQuery, result, intent, ct);
            answer += intelligentResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ResponseFormatting] Intelligent response failed, using fallback");
            answer += $"Found {result.RowCount} result(s).";
        }

        return answer;
    }
}
