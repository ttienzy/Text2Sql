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
        // Use AI to generate message
        if (_llmClient != null)
        {
            try
            {
                var aiMessage = await GenerateAIMessageAsync(result, isVietnamese, cancellationToken);
                if (!string.IsNullOrWhiteSpace(aiMessage))
                {
                    _logger.LogDebug("[ForbiddenPipeline] Using AI-generated message");
                    return aiMessage;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ForbiddenPipeline] AI message generation failed");
            }
        }

        // Simple fallback if AI unavailable
        _logger.LogDebug("[ForbiddenPipeline] Using simple fallback message");
        return isVietnamese
            ? "⚠️ Thao tác này không được phép vì sẽ xóa dữ liệu vĩnh viễn."
            : "⚠️ This operation is not allowed as it would permanently delete data.";
    }

    private async Task<string> GenerateAIMessageAsync(
        ForbiddenOperationResult result,
        bool isVietnamese,
        CancellationToken cancellationToken)
    {
        var language = isVietnamese ? "Vietnamese" : "English";
        var detectedOperation = string.Join(", ", result.DetectedPatterns ?? new List<string>());

        var systemPrompt = $@"You are a database security assistant. Generate a CONCISE warning message.

**CRITICAL RULES:**
1. Be direct and clear, NOT verbose
2. Use {language} language ONLY
3. Keep it SHORT - max 200 words
4. Use emojis: ⚠️ for warning, 💡 for tips
5. Format with markdown
6. Focus on WHY it's blocked and 2-3 safe alternatives

**Structure:**
1. Warning (1 line)
2. Brief reason (1-2 sentences)
3. 2-3 safe alternatives with SHORT examples
4. One-line tip

**Tone:**
- Direct but helpful
- Educational, not accusatory
- Concise, no fluff

Return ONLY the message, no extra text.";

        var userPrompt = $@"User query: ""{result.OriginalQuestion}""
Detected: {detectedOperation}

Generate a SHORT {language} warning that:
1. States it's blocked (1 line)
2. Explains why (1-2 sentences)
3. Lists 2-3 alternatives with brief SQL examples
4. Ends with a tip (1 line)

Keep it under 200 words.";

        var response = await _llmClient!.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        return response?.Trim() ?? string.Empty;
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
