using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// LLM-based query classifier using lightweight model (gpt-4o-mini)
/// Handles 20-30% of ambiguous queries that rule-based cannot confidently classify
/// </summary>
public class LLMQueryClassifier : ILLMQueryClassifier
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<LLMQueryClassifier> _logger;

    // System prompt for classification
    private const string SystemPrompt = @"Bạn là một bộ phân loại độ phức tạp của câu hỏi truy vấn database.

Nhiệm vụ: Phân loại câu hỏi người dùng thành 3 mức độ phức tạp:

1. **Simple** (Đơn giản): 
   - Chỉ 1 bảng, không có JOIN, không có tổng hợp
   - Ví dụ: ""liệt kê khách hàng"", ""xem đơn hàng hôm nay"", ""thông tin sản phẩm X""

2. **Medium** (Trung bình): 
   - Có JOINs, aggregation (tổng, trung bình, đếm), lọc theo thời gian, xếp hạng
   - Ví dụ: ""doanh thu tháng này"", ""top 10 khách hàng"", ""tổng đơn hàng theo nhân viên""

3. **Complex** (Phức tạp): 
   - Subqueries, phân tích xu hướng, so sánh, câu hỏi mơ hồ
   - Ví dụ: ""phân tích xu hướng"", ""so sánh cùng kỳ năm ngoái"", ""tại sao doanh thu giảm""

Trả lời theo format JSON:
```json
{
  ""complexity"": ""Simple|Medium|Complex"",
  ""confidence"": 0.0-1.0,
  ""reasoning"": ""Giải thích ngắn gọn bằng tiếng Việt""
}
```

Luôn chọn mức cao hơn nếu không chắc chắn (conservative).";

    public LLMQueryClassifier(
        ILLMClient llmClient,
        ILogger<LLMQueryClassifier> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default)
    {
        _logger.LogDebug("[LLMQueryClassifier] Classifying query: {Query}", query);

        try
        {
            var response = await _llmClient.CompleteWithSystemPromptAsync(
                SystemPrompt,
                $"Phân loại câu hỏi này:\n\n\"{query}\"",
                ct);

            return ParseLLMResponse(response, query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LLMQueryClassifier] Error classifying query: {Query}", query);

            // Fallback: conservative classification
            return new QueryClassifierResult
            {
                Complexity = QueryComplexity.Complex,
                Confidence = 0.5,
                Reasoning = $"LLM error: {ex.Message}. Conservative fallback to Complex.",
                UsedLLM = true,
                Method = ClassificationMethod.LLMBased
            };
        }
    }

    private QueryClassifierResult ParseLLMResponse(string response, string query)
    {
        try
        {
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var result = JsonSerializer.Deserialize<LLMClassificationResponse>(json);

                if (result != null)
                {
                    var complexity = result.Complexity?.ToLowerInvariant() switch
                    {
                        "simple" => QueryComplexity.Simple,
                        "medium" => QueryComplexity.Medium,
                        "complex" => QueryComplexity.Complex,
                        _ => QueryComplexity.Complex // Conservative fallback
                    };

                    _logger.LogInformation(
                        "[LLMQueryClassifier] LLM classified: {Complexity} (confidence: {Confidence})",
                        complexity,
                        result.Confidence);

                    return new QueryClassifierResult
                    {
                        Complexity = complexity,
                        Confidence = result.Confidence ?? 0.8,
                        Reasoning = result.Reasoning ?? "LLM classification",
                        UsedLLM = true,
                        Method = ClassificationMethod.LLMBased
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLMQueryClassifier] Failed to parse LLM response");
        }

        // If parsing fails, use heuristic based on query content
        return ClassifyByHeuristic(query);
    }

    private QueryClassifierResult ClassifyByHeuristic(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        // Conservative: classify as Complex if uncertain
        return new QueryClassifierResult
        {
            Complexity = QueryComplexity.Complex,
            Confidence = 0.6,
            Reasoning = "Parse failed - using heuristic classification",
            UsedLLM = true,
            Method = ClassificationMethod.LLMBased
        };
    }

    private class LLMClassificationResponse
    {
        public string? Complexity { get; set; }
        public double? Confidence { get; set; }
        public string? Reasoning { get; set; }
    }
}
