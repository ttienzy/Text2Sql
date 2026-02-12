using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Models;

public class IntentAnalysis
{
    [JsonPropertyName("intent")]
    public QueryIntent Intent { get; set; }

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("metrics")]
    public List<string> Metrics { get; set; } = new();

    [JsonPropertyName("filters")]
    public List<FilterCondition> Filters { get; set; } = new();

    [JsonPropertyName("needsClarification")]
    public bool NeedsClarification { get; set; }

    [JsonPropertyName("clarificationQuestion")]
    public string? ClarificationQuestion { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueryIntent
{
    LIST,      // "Liệt kê...", "Cho tôi danh sách..."
    COUNT,     // "Có bao nhiêu...", "Đếm..."
    AGGREGATE, // "Tổng...", "Top...", "Trung bình..."
    DETAIL,    // "Thông tin về...", "Chi tiết..."
    SCHEMA     // "Các bảng...", "Cấu trúc database..."
}

public class FilterCondition
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    // FIX: Allow both string and array for filter value
    public object? Value { get; set; }
}
