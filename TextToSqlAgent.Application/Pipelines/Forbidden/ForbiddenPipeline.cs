using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipelines.Forbidden;

/// <summary>
/// FORBIDDEN Pipeline - Hard rejection for DELETE/DROP/TRUNCATE operations
/// 
/// Flow (3 steps):
/// F1. Detect delete/destroy intent → BLOCK
/// F2. Hard reject - no SQL generation, no bypass
/// F3. Suggest safe alternatives (AI-generated message)
/// 
/// NO EXCEPTIONS - This pipeline cannot be bypassed
/// </summary>
public class ForbiddenPipeline : IForbiddenPipeline
{
    private readonly ILogger<ForbiddenPipeline> _logger;
    private readonly ILLMClient? _llmClient;

    public ForbiddenPipeline(
        ILogger<ForbiddenPipeline> logger,
        ILLMClient? llmClient = null)
    {
        _logger = logger;
        _llmClient = llmClient;
    }

    /// <summary>
    /// F1-F3: Reject forbidden operation immediately
    /// </summary>
    public async Task<ForbiddenOperationResult> RejectAsync(
        string question,
        IntentClassificationResult intentResult,
        CancellationToken cancellationToken = default)
    {
        // Detect language from question
        var isVietnamese = ContainsVietnamese(question);

        // F1: Log detection
        _logger.LogWarning(
            "[ForbiddenPipeline] BLOCKED forbidden operation: {Question} | Reason: {Reason} | Language: {Language}",
            question,
            intentResult.ForbiddenReason ?? "Destructive operation detected",
            isVietnamese ? "Vietnamese" : "English");

        // F2: Build rejection result
        var result = new ForbiddenOperationResult
        {
            IsBlocked = true,
            OriginalQuestion = question,
            RejectionReason = intentResult.ForbiddenReason
                ?? (isVietnamese
                    ? "Thao tác này sẽ xóa dữ liệu vĩnh viễn và không được phép"
                    : "This operation would permanently delete data and is not allowed"),
            DetectedPatterns = intentResult.MatchedKeywords,
            IntentClassification = intentResult,
            RejectedAt = DateTime.UtcNow
        };

        // F3: Add safe alternatives (language-specific)
        result.SafeAlternatives = intentResult.SafeAlternatives.Count > 0
            ? intentResult.SafeAlternatives
            : GetDefaultSafeAlternatives(isVietnamese);

        // Build user-facing message with AI
        result.UserFacingMessage = await BuildUserFacingMessageAsync(
            result,
            isVietnamese,
            cancellationToken);

        _logger.LogInformation(
            "[ForbiddenPipeline] Rejection complete. Suggested {Count} safe alternatives",
            result.SafeAlternatives.Count);

        return result;
    }

    private async Task<string> BuildUserFacingMessageAsync(
        ForbiddenOperationResult result,
        bool isVietnamese,
        CancellationToken cancellationToken)
    {
        // Use AI to generate contextual message with markdown
        if (_llmClient != null)
        {
            try
            {
                _logger.LogDebug("[ForbiddenPipeline] Generating AI message with markdown");
                return await GenerateAIMessageAsync(result, isVietnamese, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ForbiddenPipeline] AI generation failed, using fallback template");
            }
        }

        // Fallback to static template if AI unavailable
        _logger.LogDebug("[ForbiddenPipeline] Using static template message");
        var tableName = ExtractTableName(result.OriginalQuestion);

        if (!string.IsNullOrEmpty(tableName))
        {
            return ForbiddenMessageTemplates.GetCustomMessage(
                string.Join(", ", result.DetectedPatterns ?? new List<string>()),
                isVietnamese,
                tableName
            );
        }

        return isVietnamese
            ? ForbiddenMessageTemplates.GetVietnameseMessage(
                string.Join(", ", result.DetectedPatterns ?? new List<string>()))
            : ForbiddenMessageTemplates.GetEnglishMessage(
                string.Join(", ", result.DetectedPatterns ?? new List<string>()));
    }

    private async Task<string> GenerateAIMessageAsync(
        ForbiddenOperationResult result,
        bool isVietnamese,
        CancellationToken cancellationToken)
    {
        var language = isVietnamese ? "Vietnamese" : "English";
        var tableName = ExtractTableName(result.OriginalQuestion);

        var systemPrompt = $@"You are a database security assistant. Generate a clear, helpful warning message.

**CRITICAL RULES:**
1. Use {language} language ONLY
2. Keep it SHORT and actionable (max 250 words)
3. Use markdown for better readability:
   - Use **bold** for important warnings
   - Use ```sql code blocks for SQL examples
   - Use bullet points for lists
   - Use emojis: ⚠️ for warning, 💡 for tips, ✅ for recommendations
4. Focus on WHY it's blocked and provide 2-3 concrete alternatives
5. Make SQL examples specific to the user's context when possible

**Structure:**
1. Clear warning with emoji
2. Brief explanation of the risk
3. 2-3 safe alternatives with SQL examples
4. Helpful tip at the end";

        var userPrompt = $@"Generate a {language} security warning for this blocked operation:

**Original Question:** {result.OriginalQuestion}
**Detected Patterns:** {string.Join(", ", result.DetectedPatterns ?? new List<string>())}
**Table Name:** {(string.IsNullOrEmpty(tableName) ? "unknown" : tableName)}
**Reason:** {result.RejectionReason}

Provide specific, actionable alternatives with real SQL examples.";

        var response = await _llmClient!.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken
        );

        return response?.Trim() ?? result.RejectionReason;
    }

    private string ExtractTableName(string question)
    {
        // Simple extraction - look for common patterns
        var lowerQuestion = question.ToLowerInvariant();

        // Pattern: "delete from <table>"
        var deleteFromMatch = System.Text.RegularExpressions.Regex.Match(
            lowerQuestion,
            @"delete\s+from\s+(\w+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (deleteFromMatch.Success)
        {
            return deleteFromMatch.Groups[1].Value;
        }

        // Pattern: "xóa <table>" or "xoá <table>"
        var vietnameseMatch = System.Text.RegularExpressions.Regex.Match(
            lowerQuestion,
            @"(?:xóa|xoá)\s+(?:bảng\s+)?(\w+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (vietnameseMatch.Success)
        {
            return vietnameseMatch.Groups[1].Value;
        }

        // Pattern: "drop table <table>"
        var dropMatch = System.Text.RegularExpressions.Regex.Match(
            lowerQuestion,
            @"drop\s+table\s+(\w+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (dropMatch.Success)
        {
            return dropMatch.Groups[1].Value;
        }

        return string.Empty;
    }

    private bool ContainsVietnamese(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lowerText = text.ToLowerInvariant();

        // Check for Vietnamese keywords
        var vietnameseKeywords = new[]
        {
            "xóa", "xoá", "thêm", "sửa", "cập nhật", "tạo", "đổi",
            "liệt kê", "tìm", "xem", "danh sách", "bảng", "cột",
            "người dùng", "khách hàng", "đơn hàng", "sản phẩm",
            "tất cả", "toàn bộ", "hết", "cho tôi", "hiển thị"
        };

        return vietnameseKeywords.Any(keyword => lowerText.Contains(keyword));
    }

    private List<SafeAlternative> GetDefaultSafeAlternatives(bool isVietnamese)
    {
        // Return empty list - AI will generate alternatives in the message
        return new List<SafeAlternative>();
    }
}
