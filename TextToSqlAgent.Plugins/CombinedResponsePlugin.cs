using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Plugins;

/// <summary>
/// PHASE-2 TASK 2.2c: Combined plugin that generates both intelligent response AND suggestions in a single LLM call.
/// Reduces latency by 1-3s by eliminating one LLM roundtrip.
/// </summary>
public class CombinedResponsePlugin
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<CombinedResponsePlugin> _logger;

    public CombinedResponsePlugin(ILLMClient llmClient, ILogger<CombinedResponsePlugin> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    [KernelFunction, Description("Generate intelligent response and contextual suggestions in a single LLM call")]
    public async Task<CombinedResponseResult> GenerateCombinedResponseAsync(
        [Description("User's original question")] string originalQuestion,
        [Description("SQL query that was executed")] string sqlQuery,
        [Description("Query execution result")] SqlExecutionResult queryResult,
        [Description("Intent analysis")] IntentAnalysis intent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var systemPrompt = BuildCombinedSystemPrompt();
            var userPrompt = BuildCombinedUserPrompt(originalQuestion, sqlQuery, queryResult, intent);

            _logger.LogDebug("[CombinedResponse] Generating response + suggestions in single LLM call");

            var response = await _llmClient.CompleteWithSystemPromptAsync(
                systemPrompt, userPrompt, cancellationToken);

            var result = ParseCombinedResponse(response, queryResult, intent);

            _logger.LogInformation(
                "[CombinedResponse] Generated response ({ResponseLength} chars) + {SuggestionCount} suggestions",
                result.IntelligentAnswer.Length, result.Suggestions.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CombinedResponse] Failed to generate combined response, using fallback");
            return GenerateFallbackResponse(queryResult, intent, originalQuestion);
        }
    }

    private static string BuildCombinedSystemPrompt()
    {
        return @"Bạn là một chuyên gia phân tích dữ liệu thông minh, giúp người dùng hiểu kết quả truy vấn cơ sở dữ liệu.

# NHIỆM VỤ CỦA BẠN
Tạo phản hồi thông minh VÀ đề xuất các câu hỏi tiếp theo có liên quan trong MỘT phản hồi duy nhất.

# PHẦN 1: PHẢN HỒI THÔNG MINH

## NGUYÊN TẮC PHẢN HỒI
1. **Sử dụng ngôn ngữ tự nhiên**: Trả lời như một chuyên gia phân tích dữ liệu
2. **Cung cấp insight**: Phân tích ý nghĩa của dữ liệu, không chỉ liệt kê
3. **Ngữ cảnh kinh doanh**: Giải thích tác động và ý nghĩa thực tế
4. **Ngôn ngữ phù hợp**: Sử dụng tiếng Việt nếu câu hỏi bằng tiếng Việt
5. **Tóm tắt thông minh**: Highlight những điểm quan trọng nhất

## CÁCH PHẢN HỒI THEO LOẠI TRUY VẤN

### COUNT (Đếm)
- Không nói ""Found X records"" → Nói ""Hiện có X [đối tượng] trong hệ thống""
- Đưa ra đánh giá về số lượng (nhiều/ít, bình thường/bất thường)

### TOP_N (Xếp hạng)
- Không liệt kê từng item → Phân tích xu hướng và pattern
- Highlight điểm đặc biệt của top items
- So sánh giữa các items hàng đầu

### LIST (Danh sách)
- Tóm tắt đặc điểm chung của danh sách
- Nhóm theo category hoặc pattern nếu có
- Đưa ra observation về dữ liệu

### AGGREGATE (Tổng hợp)
- Giải thích ý nghĩa của các con số
- So sánh với benchmark hoặc kỳ vọng
- Đưa ra insight về performance

### COMPARISON (So sánh)
- Phân tích xu hướng tăng/giảm
- Giải thích nguyên nhân có thể
- Đánh giá tác động kinh doanh

# PHẦN 2: ĐỀ XUẤT CÂU HỎI TIẾP THEO

## NGUYÊN TẮC ĐỀ XUẤT
1. **Liên quan trực tiếp**: Dựa trên kết quả hiện tại
2. **Khám phá sâu hơn**: Drill-down vào chi tiết
3. **Mở rộng phạm vi**: Phân tích các khía cạnh liên quan
4. **Hành động tiếp theo**: Câu hỏi logic kế tiếp trong workflow

## LOẠI ĐỀ XUẤT THEO INTENT

### COUNT → Đề xuất:
- Phân tích theo thời gian (trend)
- Phân tích theo nhóm (breakdown)
- So sánh với kỳ trước

### TOP_N → Đề xuất:
- Chi tiết về top item
- So sánh top items
- Phân tích bottom items

### LIST → Đề xuất:
- Lọc theo tiêu chí
- Sắp xếp theo thuộc tính khác
- Tổng hợp thống kê

### AGGREGATE → Đề xuất:
- Breakdown theo dimension
- Trend analysis
- Comparison với benchmark

## YÊU CẦU ĐỀ XUẤT
- Đưa ra CHÍNH XÁC 3 câu hỏi
- Mỗi câu hỏi phải CỤ THỂ và CÓ THỂ THỰC HIỆN
- Sử dụng tên bảng/cột THỰC TẾ từ schema
- Ngôn ngữ nhất quán với câu hỏi gốc

# ĐỊNH DẠNG PHẢN HỒI (JSON)

Bạn PHẢI trả về JSON với format sau:

```json
{
  ""answer"": ""Phản hồi thông minh của bạn (2-4 câu)"",
  ""suggestions"": [
    ""Câu hỏi đề xuất 1"",
    ""Câu hỏi đề xuất 2"",
    ""Câu hỏi đề xuất 3""
  ]
}
```

# VÍ DỤ PHẢN HỒI TỐT

**Input**: ""Liệt kê top 5 sản phẩm bán chạy""
**Output**:
```json
{
  ""answer"": ""iPhone 15 Pro dẫn đầu với 120 đơn vị bán ra, vượt xa Samsung Galaxy (95 đơn vị). Sản phẩm Apple chiếm ưu thế rõ rệt trong phân khúc cao cấp với khoảng cách đáng kể so với đối thủ."",
  ""suggestions"": [
    ""Doanh thu của iPhone 15 Pro trong tháng này là bao nhiêu?"",
    ""So sánh doanh số iPhone 15 Pro với tháng trước"",
    ""Top 5 sản phẩm bán chậm nhất là gì?""
  ]
}
```

# LƯU Ý QUAN TRỌNG
- LUÔN trả về JSON hợp lệ
- KHÔNG bao giờ chỉ liệt kê dữ liệu thô trong answer
- LUÔN cung cấp CHÍNH XÁC 3 suggestions
- Suggestions phải CỤ THỂ và liên quan đến dữ liệu hiện tại
- Sử dụng ngôn ngữ nhất quán (Việt hoặc Anh)";
    }

    private static string BuildCombinedUserPrompt(
        string originalQuestion,
        string sqlQuery,
        SqlExecutionResult queryResult,
        IntentAnalysis intent)
    {
        var prompt = $@"Câu hỏi gốc: ""{originalQuestion}""

Loại truy vấn: {intent.Intent}
Đối tượng chính: {intent.Target}

SQL đã thực thi:
```sql
{sqlQuery}
```

Kết quả SQL:
- Số dòng: {queryResult.RowCount}
- Các cột: {string.Join(", ", queryResult.Columns)}";

        // Thêm dữ liệu mẫu (tối đa 5 dòng đầu)
        if (queryResult.Rows?.Count > 0)
        {
            prompt += "\n- Dữ liệu mẫu (5 dòng đầu):";
            var sampleRows = queryResult.Rows.Take(5);
            foreach (var row in sampleRows)
            {
                var values = queryResult.Columns.Select(col =>
                    row.ContainsKey(col) ? row[col]?.ToString() ?? "null" : "null");
                prompt += $"\n  [{string.Join(", ", values)}]";
            }
        }

        prompt += @"

Hãy tạo:
1. Phản hồi thông minh phân tích ý nghĩa của kết quả
2. 3 câu hỏi đề xuất liên quan và có thể thực hiện

Trả về JSON theo format đã chỉ định:";

        return prompt;
    }

    private CombinedResponseResult ParseCombinedResponse(
        string response,
        SqlExecutionResult queryResult,
        IntentAnalysis intent)
    {
        try
        {
            // Try to parse JSON response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<CombinedResponseJson>(jsonStr,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Answer))
                {
                    return new CombinedResponseResult
                    {
                        IntelligentAnswer = parsed.Answer.Trim(),
                        Suggestions = parsed.Suggestions?.Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim()).ToList() ?? new List<string>()
                    };
                }
            }

            _logger.LogWarning("[CombinedResponse] Failed to parse JSON, using fallback");
            return GenerateFallbackResponse(queryResult, intent, "");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CombinedResponse] JSON parsing failed, using fallback");
            return GenerateFallbackResponse(queryResult, intent, "");
        }
    }

    private static CombinedResponseResult GenerateFallbackResponse(
        SqlExecutionResult queryResult,
        IntentAnalysis intent,
        string originalQuestion)
    {
        // Fallback response when LLM fails
        var answer = intent.Intent switch
        {
            QueryIntent.COUNT => $"Tìm thấy {queryResult.Rows?[0]?.Values?.FirstOrDefault()} bản ghi trong hệ thống.",
            QueryIntent.TOP_N => $"Đã xác định được {queryResult.RowCount} mục hàng đầu theo tiêu chí yêu cầu.",
            QueryIntent.LIST => $"Truy xuất thành công {queryResult.RowCount} bản ghi từ cơ sở dữ liệu.",
            QueryIntent.AGGREGATE => $"Phân tích dữ liệu hoàn tất với {queryResult.RowCount} kết quả tổng hợp.",
            _ => $"Truy vấn thành công với {queryResult.RowCount} kết quả."
        };

        // Fallback suggestions
        var suggestions = new List<string>();
        if (!string.IsNullOrWhiteSpace(intent.Target))
        {
            suggestions.Add($"Hiển thị chi tiết của {intent.Target}");
            suggestions.Add($"Thống kê {intent.Target} theo nhóm");
            suggestions.Add($"So sánh {intent.Target} với kỳ trước");
        }

        return new CombinedResponseResult
        {
            IntelligentAnswer = answer,
            Suggestions = suggestions
        };
    }

    private class CombinedResponseJson
    {
        public string Answer { get; set; } = "";
        public List<string>? Suggestions { get; set; }
    }
}

/// <summary>
/// Result containing both intelligent answer and contextual suggestions.
/// </summary>
public class CombinedResponseResult
{
    public string IntelligentAnswer { get; set; } = "";
    public List<string> Suggestions { get; set; } = new();
}
