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

        // ✅ T7: Fast-path template response for simple queries
        string answer;
        List<string> suggestions;

        var intentType = context.Intent?.Intent;
        var isSimpleIntent = intentType is QueryIntent.LIST or QueryIntent.COUNT or QueryIntent.DETAIL;
        var complexityScore = context.IntentClassification?.ComplexityScore ?? 1.0;

        // Use template if: explicit simple intent OR (unknown intent + low complexity)
        var useTemplate = isSimpleIntent
            || (intentType == QueryIntent.Unknown && complexityScore < 0.5);

        if (useTemplate && context.ExecutionResult is { Success: true })
        {
            // ── Template Response Path (no LLM) ──
            answer = BuildTemplateResponse(context, intentType, finalSql);
            suggestions = BuildRuleBasedSuggestions(context);

            _logger.LogInformation(
                "[ResponseFormatting] ⚡ Template response used for {Intent} (skip LLM, complexity: {Score:F2})",
                intentType, complexityScore);
        }
        else
        {
            // ── LLM Response Path (complex queries) ──
            (answer, suggestions) = await GenerateLlmResponseAsync(context, finalSql, ct);
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

    #region T7: Template Response Helpers

    /// <summary>
    /// Build a deterministic template response for simple queries (no LLM).
    /// Language: defaults to Vietnamese; falls back to English if no Vietnamese chars detected.
    /// </summary>
    private string BuildTemplateResponse(PipelineContext context, QueryIntent? intentType, string finalSql)
    {
        var result = context.ExecutionResult!;
        var rowCount = result.RowCount;

        // Language detection — check enriched question for Vietnamese chars
        var isVietnamese = ContainsVietnamese(context.EnrichedQuestion);

        // Correction prefix
        var prefix = context.Corrections.Any()
            ? $"ℹ️  SQL was auto-corrected {context.Corrections.Count} time(s).\n"
            : "";

        // Multi-turn prefix
        if (context.ConversationCtx is { TurnCount: > 1 })
        {
            prefix += "📊 ";
        }

        if (rowCount == 0)
        {
            return prefix + (isVietnamese
                ? "Không tìm thấy kết quả nào. Hãy thử điều chỉnh bộ lọc hoặc tiêu chí tìm kiếm."
                : "No results found. Try adjusting your filters or search criteria.");
        }

        return intentType switch
        {
            QueryIntent.COUNT => prefix + (isVietnamese
                ? $"Truy vấn thành công. Có **{FormatCountValue(result)}** bản ghi thỏa mãn điều kiện."
                : $"Query successful. Found **{FormatCountValue(result)}** matching record(s)."),

            QueryIntent.LIST => prefix + (isVietnamese
                ? $"Tìm thấy **{rowCount}** kết quả. Chi tiết như bảng bên dưới:"
                : $"Found **{rowCount}** result(s). Details shown in the table below:"),

            QueryIntent.DETAIL => prefix + (isVietnamese
                ? $"Tìm thấy **{rowCount}** kết quả chi tiết:"
                : $"Found **{rowCount}** detailed result(s):"),

            _ => prefix + (isVietnamese
                ? $"Tìm thấy **{rowCount}** kết quả."
                : $"Found **{rowCount}** result(s).")
        };
    }

    /// <summary>
    /// Extract the count value from a COUNT query result (first cell of first row).
    /// </summary>
    private static string FormatCountValue(SqlExecutionResult result)
    {
        if (result.Rows != null && result.Rows.Count > 0)
        {
            var firstRow = result.Rows[0];
            if (firstRow.Count > 0)
            {
                var firstValue = firstRow.Values.FirstOrDefault();
                if (firstValue != null)
                    return firstValue.ToString() ?? result.RowCount.ToString();
            }
        }
        return result.RowCount.ToString();
    }

    /// <summary>
    /// Build rule-based suggestions without LLM.
    /// </summary>
    private List<string> BuildRuleBasedSuggestions(PipelineContext context)
    {
        var ruleBasedService = new RuleBasedSuggestionService();
        return ruleBasedService.Generate(
            context.Intent!.Intent, context.Intent.Target, context.UserQuestion);
    }

    private static bool ContainsVietnamese(string text)
    {
        return !string.IsNullOrEmpty(text) && text.Any(c =>
            "àáảãạăắằẳẵặâấầẩẫậèéẻẽẹêếềểễệìíỉĩịòóỏõọôốồổỗộơớờởỡợùúủũụưứừửữựỳýỷỹỵđ".Contains(c));
    }

    #endregion

    #region LLM Response Path (Complex queries)

    /// <summary>
    /// Generate response via CombinedResponsePlugin LLM call (existing behavior for complex queries).
    /// </summary>
    private async Task<(string answer, List<string> suggestions)> GenerateLlmResponseAsync(
        PipelineContext context, string finalSql, CancellationToken ct)
    {
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
                "[ResponseFormatting] ⚡ Generated response + {Count} suggestions in single LLM call",
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
                var sqlGenerator = _serviceFactory.GetSqlGenerator();
                suggestions = await sqlGenerator.GenerateContextualSuggestionsAsync(
                    context.UserQuestion, finalSql, context.ExecutionResult!, context.Intent!, ct);
            }
            catch
            {
                suggestions = new List<string>();
            }
        }

        return (answer, suggestions);
    }

    #endregion

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
