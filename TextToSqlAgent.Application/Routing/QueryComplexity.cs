namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Query complexity tier for routing to appropriate pipeline
/// </summary>
public enum QueryComplexity
{
    /// <summary>
    /// Single table, no joins, no aggregation
    /// Examples: "liệt kê khách hàng", "xem đơn hàng hôm nay"
    /// </summary>
    Simple,

    /// <summary>
    /// JOINs, aggregation, time filters, ranking
    /// Examples: "doanh thu tháng này", "top 10 khách hàng"
    /// </summary>
    Medium,

    /// <summary>
    /// Subqueries, trend analysis, comparisons, ambiguous
    /// Examples: "phân tích xu hướng", "so sánh cùng kỳ năm ngoái"
    /// </summary>
    Complex
}
