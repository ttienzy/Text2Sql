using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Plugins;

/// <summary>
/// Plugin tạo phản hồi thông minh và có ngữ cảnh từ kết quả SQL
/// </summary>
public class IntelligentResponsePlugin
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<IntelligentResponsePlugin> _logger;

    public IntelligentResponsePlugin(ILLMClient llmClient, ILogger<IntelligentResponsePlugin> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    [KernelFunction, Description("Generate intelligent contextual response from SQL results")]
    public async Task<string> GenerateResponseAsync(
        [Description("User's original question")] string originalQuestion,
        [Description("SQL query that was executed")] string sqlQuery,
        [Description("Query execution result")] SqlExecutionResult queryResult,
        [Description("Intent analysis")] IntentAnalysis intent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(originalQuestion, sqlQuery, queryResult, intent);

            var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, cancellationToken);

            _logger.LogInformation("Generated intelligent response for query: {Query}", originalQuestion);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating intelligent response for query: {Query}", originalQuestion);

            // Fallback to simple response
            return GenerateFallbackResponse(queryResult, intent);
        }
    }

    private static string BuildSystemPrompt()
    {
        return @"Bạn là một chuyên gia phân tích dữ liệu thông minh, giúp người dùng hiểu kết quả truy vấn cơ sở dữ liệu.

# NHIỆM VỤ CỦA BẠN
Tạo phản hồi thông minh, có ý nghĩa và dễ hiểu từ kết quả SQL, KHÔNG chỉ liệt kê dữ liệu thô.

# NGUYÊN TẮC PHẢN HỒI
1. **Sử dụng ngôn ngữ tự nhiên**: Trả lời như một chuyên gia phân tích dữ liệu
2. **Cung cấp insight**: Phân tích ý nghĩa của dữ liệu, không chỉ liệt kê
3. **Ngữ cảnh kinh doanh**: Giải thích tác động và ý nghĩa thực tế
4. **Ngôn ngữ phù hợp**: Sử dụng tiếng Việt nếu câu hỏi bằng tiếng Việt
5. **Tóm tắt thông minh**: Highlight những điểm quan trọng nhất

# CÁCH PHẢN HỒI THEO LOẠI TRUY VẤN

## COUNT (Đếm)
- Không nói ""Found X records"" → Nói ""Hiện có X [đối tượng] trong hệ thống""
- Đưa ra đánh giá về số lượng (nhiều/ít, bình thường/bất thường)

## TOP_N (Xếp hạng)
- Không liệt kê từng item → Phân tích xu hướng và pattern
- Highlight điểm đặc biệt của top items
- So sánh giữa các items hàng đầu

## LIST (Danh sách)
- Tóm tắt đặc điểm chung của danh sách
- Nhóm theo category hoặc pattern nếu có
- Đưa ra observation về dữ liệu

## AGGREGATE (Tổng hợp)
- Giải thích ý nghĩa của các con số
- So sánh với benchmark hoặc kỳ vọng
- Đưa ra insight về performance

## COMPARISON (So sánh)
- Phân tích xu hướng tăng/giảm
- Giải thích nguyên nhân có thể
- Đánh giá tác động kinh doanh

# ĐỊNH DẠNG PHẢN HỒI
- **Câu mở đầu**: Tóm tắt kết quả chính (1 câu)
- **Phân tích**: Insight và ý nghĩa (2-3 câu)
- **Highlight**: Điểm đáng chú ý nhất (1 câu)

# VÍ DỤ PHẢN HỒI TỐT

**Thay vì**: ""Top 5 results: #1: iPhone 15 Pro | 120 | #2: Samsung Galaxy | 95""
**Nên**: ""iPhone 15 Pro dẫn đầu với 120 đơn vị bán ra, vượt xa Samsung Galaxy (95 đơn vị). Sản phẩm Apple chiếm ưu thế rõ rệt trong phân khúc cao cấp với khoảng cách đáng kể so với đối thủ.""

**Thay vì**: ""Retrieved 150 records. Preview: Customer A | 2023 | Customer B | 2023""
**Nên**: ""Hệ thống hiện có 150 khách hàng đang hoạt động. Phần lớn là khách hàng mới tham gia trong năm 2023, cho thấy tốc độ tăng trưởng tích cực của doanh nghiệp.""

# LƯU Ý QUAN TRỌNG
- KHÔNG bao giờ chỉ liệt kê dữ liệu thô
- LUÔN cung cấp insight và ý nghĩa
- Sử dụng ngôn ngữ kinh doanh, không kỹ thuật
- Giữ phản hồi ngắn gọn nhưng có giá trị (2-4 câu)
- Tập trung vào điều quan trọng nhất với người dùng

# NGÔN NGỮ
- Nếu câu hỏi bằng tiếng Việt → Trả lời bằng tiếng Việt
- Nếu câu hỏi bằng tiếng Anh → Trả lời bằng tiếng Anh
- Sử dụng thuật ngữ kinh doanh phù hợp";
    }

    private static string BuildUserPrompt(string originalQuestion, string sqlQuery, SqlExecutionResult queryResult, IntentAnalysis intent)
    {
        var prompt = $@"Câu hỏi gốc: ""{originalQuestion}""

Loại truy vấn: {intent.Intent}
Đối tượng chính: {intent.Target}

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

Hãy tạo phản hồi thông minh và có ý nghĩa cho người dùng. KHÔNG liệt kê dữ liệu thô, mà hãy phân tích và giải thích ý nghĩa của kết quả.

Phản hồi của bạn:";

        return prompt;
    }

    private static string GenerateFallbackResponse(SqlExecutionResult queryResult, IntentAnalysis intent)
    {
        // Fallback response khi LLM không khả dụng
        return intent.Intent switch
        {
            QueryIntent.COUNT => $"Tìm thấy {queryResult.Rows?[0]?.Values?.FirstOrDefault()} bản ghi trong hệ thống.",
            QueryIntent.TOP_N => $"Đã xác định được {queryResult.RowCount} mục hàng đầu theo tiêu chí yêu cầu.",
            QueryIntent.LIST => $"Truy xuất thành công {queryResult.RowCount} bản ghi từ cơ sở dữ liệu.",
            QueryIntent.AGGREGATE => $"Phân tích dữ liệu hoàn tất với {queryResult.RowCount} kết quả tổng hợp.",
            _ => $"Truy vấn thành công với {queryResult.RowCount} kết quả."
        };
    }
}