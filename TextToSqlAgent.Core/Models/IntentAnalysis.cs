using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Models;

public class IntentAnalysis
{
    [JsonPropertyName("intent")]
    public QueryIntent Intent { get; set; }

    [JsonPropertyName("complexity")]
    public string Complexity { get; set; } = "Simple";

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("relatedEntities")]
    public List<string> RelatedEntities { get; set; } = new();

    [JsonPropertyName("metrics")]
    public List<MetricDefinition> Metrics { get; set; } = new();

    [JsonPropertyName("filters")]
    public List<FilterCondition> Filters { get; set; } = new();

    [JsonPropertyName("groupBy")]
    public List<string> GroupBy { get; set; } = new();

    [JsonPropertyName("orderBy")]
    public List<OrderByClause> OrderBy { get; set; } = new();

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("requiredFeatures")]
    public List<string> RequiredFeatures { get; set; } = new();

    [JsonPropertyName("timeRange")]
    public TimeRangeFilter TimeRange { get; set; } = new();

    [JsonPropertyName("needsClarification")]
    public bool NeedsClarification { get; set; }

    [JsonPropertyName("clarificationQuestion")]
    public string? ClarificationQuestion { get; set; }
}

public class MetricDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("calculation")]
    public string Calculation { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;
}

public class OrderByClause
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "ASC";
}

public class TimeRangeFilter
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "none";

    [JsonPropertyName("relativeType")]
    public string? RelativeType { get; set; }

    [JsonPropertyName("relativeDays")]
    public int? RelativeDays { get; set; }

    [JsonPropertyName("absoluteStart")]
    public string? AbsoluteStart { get; set; }

    [JsonPropertyName("absoluteEnd")]
    public string? AbsoluteEnd { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueryIntent
{
    // SIMPLE INTENTS
    LIST,      // "Liệt kê...", "Cho tôi danh sách..."
    COUNT,     // "Có bao nhiêu...", "Đếm..."
    DETAIL,    // "Thông tin về...", "Chi tiết..."
    SCHEMA,    // "Các bảng...", "Cấu trúc database..."

    // AGGREGATE INTENTS
    AGGREGATE, // Generic aggregation (backward compatibility)
    SUM,       // "Tổng...", "Cộng dồn..."
    AVG,       // "Trung bình..."
    MIN_MAX,   // "Cao nhất...", "Thấp nhất..."
    TOP_N,     // "Top 10...", "N đầu tiên..."
    GROUP_BY,  // "Nhóm theo...", "Phân loại..."

    // ANALYTICAL INTENTS (Advanced)
    TREND,            // Time-series analysis
    COMPARISON,       // YoY, MoM, QoQ
    RANKING,          // Position ranking
    RUNNING_TOTAL,    // Cumulative sum
    PERCENTAGE,       // Share calculation
    MOVING_AVERAGE,   // Rolling average
    TOP_PER_GROUP,    // Best in each category

    // COMPLEX INTENTS (Very Advanced)
    MULTI_AGGREGATE,  // Multiple calculations in one query
    NESTED_ANALYSIS,  // Subqueries with aggregates
    PIVOT,            // Cross-tabulation
    COHORT            // Cohort analysis
}

public class FilterCondition
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("valueType")]
    public string ValueType { get; set; } = "literal";

    [JsonPropertyName("logicalOperator")]
    public string LogicalOperator { get; set; } = "AND";
}
