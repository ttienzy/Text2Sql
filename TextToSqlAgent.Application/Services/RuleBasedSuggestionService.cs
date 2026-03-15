using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Rule-based suggestion generator as fallback when LLM doesn't provide suggestions
/// </summary>
public class RuleBasedSuggestionService
{
    /// <summary>
    /// Generate rule-based suggestions based on query intent and target table
    /// </summary>
    public List<string> Generate(QueryIntent intent, string targetTable, string? userLanguage = null)
    {
        var isVietnamese = IsVietnamese(userLanguage);

        return intent switch
        {
            QueryIntent.COUNT => GenerateCountSuggestions(targetTable, isVietnamese),
            QueryIntent.LIST => GenerateListSuggestions(targetTable, isVietnamese),
            QueryIntent.GROUP_BY => GenerateGroupBySuggestions(targetTable, isVietnamese),
            QueryIntent.AGGREGATE => GenerateAggregateSuggestions(targetTable, isVietnamese),
            QueryIntent.TOP_N => GenerateTopNSuggestions(targetTable, isVietnamese),
            QueryIntent.COMPARISON => GenerateComparisonSuggestions(targetTable, isVietnamese),
            QueryIntent.RANKING => GenerateRankingSuggestions(targetTable, isVietnamese),
            _ => GenerateGenericSuggestions(targetTable, isVietnamese)
        };
    }

    private static bool IsVietnamese(string? userLanguage)
    {
        if (string.IsNullOrEmpty(userLanguage)) return false;

        var vietnameseKeywords = new[] { "hiển thị", "lấy", "đếm", "tổng", "so sánh", "doanh thu", "đơn hàng" };
        return vietnameseKeywords.Any(kw => userLanguage.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GenerateCountSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Hiển thị chi tiết từ bảng {table}",
                $"Nhóm {table} theo danh mục",
                $"So sánh số lượng theo thời gian"
            };
        }

        return new List<string>
        {
            $"Show details from {table} table",
            $"Group {table} by category",
            $"Compare counts over time"
        };
    }

    private static List<string> GenerateListSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Đếm tổng số {table}",
                $"Lọc {table} theo điều kiện",
                $"Sắp xếp {table} theo giá trị"
            };
        }

        return new List<string>
        {
            $"Count total {table}",
            $"Filter {table} by criteria",
            $"Sort {table} by value"
        };
    }

    private static List<string> GenerateGroupBySuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Chi tiết từng nhóm {table}",
                $"So sánh giữa các nhóm",
                $"Top nhóm có giá trị cao nhất"
            };
        }

        return new List<string>
        {
            $"Show details for each {table} group",
            $"Compare between groups",
            $"Top groups with highest values"
        };
    }

    private static List<string> GenerateAggregateSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Phân tích xu hướng {table}",
                $"So sánh theo thời gian",
                $"Chi tiết từng mục"
            };
        }

        return new List<string>
        {
            $"Analyze {table} trends",
            $"Compare over time periods",
            $"Break down by details"
        };
    }

    private static List<string> GenerateTopNSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Xem tất cả {table}",
                $"So sánh với mức trung bình",
                $"Phân tích nguyên nhân"
            };
        }

        return new List<string>
        {
            $"Show all {table}",
            $"Compare with average",
            $"Analyze contributing factors"
        };
    }

    private static List<string> GenerateComparisonSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Xu hướng theo tháng",
                $"Phân tích tăng trưởng",
                $"Chi tiết từng kỳ"
            };
        }

        return new List<string>
        {
            $"Monthly trends",
            $"Growth analysis",
            $"Period-by-period details"
        };
    }

    private static List<string> GenerateRankingSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Phân tích top và bottom",
                $"So sánh với trung bình",
                $"Xu hướng xếp hạng"
            };
        }

        return new List<string>
        {
            $"Analyze top vs bottom",
            $"Compare with average",
            $"Ranking trends"
        };
    }

    private static List<string> GenerateGenericSuggestions(string table, bool isVietnamese)
    {
        if (isVietnamese)
        {
            return new List<string>
            {
                $"Thống kê tổng quan {table}",
                $"Lọc theo điều kiện",
                $"Phân tích chi tiết"
            };
        }

        return new List<string>
        {
            $"Overall {table} statistics",
            $"Filter by conditions",
            $"Detailed analysis"
        };
    }
}