using System.Text.RegularExpressions;

namespace TextToSqlAgent.Infrastructure.Analysis;

public class QueryPatternMatcher
{
    private static readonly Dictionary<string, QueryPattern> Patterns = new()
    {
        ["count_with_having"] = new QueryPattern
        {
            Regex = @"(\w+)\s+(with|có|có\s+số)\s+(more|less|nhiều|ít|trên|dưới)\s+than\s+(\d+)\s+(\w+)",
            Intent = "AGGREGATE",
            Template = "COUNT_HAVING",
            Example = "Customers with more than 5 orders"
        },
        ["top_n_by_metric"] = new QueryPattern
        {
            Regex = @"(top|bottom|cao\s+nhất|thấp\s+nhất)\s+(\d+)\s+(\w+)\s+(by|theo)\s+(\w+)",
            Intent = "AGGREGATE",
            Template = "TOP_N",
            Example = "Top 10 customers by revenue"
        },
        ["total_sum"] = new QueryPattern
        {
            Regex = @"(total|sum|tổng|tổng\s+cộng)\s+(\w+)",
            Intent = "AGGREGATE",
            Template = "SIMPLE_AGG",
            Example = "Total revenue"
        },
        ["average"] = new QueryPattern
        {
            Regex = @"(average|avg|trung\s+bình)\s+(\w+)",
            Intent = "AGGREGATE",
            Template = "SIMPLE_AGG",
            Example = "Average order value"
        },
        ["count_all"] = new QueryPattern
        {
            Regex = @"(count|how\s+many|bao\s+nhiêu|đếm|số\s+lượng)\s+(\w+)",
            Intent = "COUNT",
            Template = "SIMPLE_COUNT",
            Example = "Count customers"
        },
        ["list_all"] = new QueryPattern
        {
            Regex = @"(show|list|display|hiển\s+thị|liệt\s+kê|danh\s+sách)\s+(all\s+)?(\w+)",
            Intent = "LIST",
            Template = "SIMPLE_LIST",
            Example = "Show all customers"
        }
    };

    public QueryPattern? Match(string question)
    {
        var normalizedQuestion = question.ToLowerInvariant();

        foreach (var (key, pattern) in Patterns)
        {
            if (Regex.IsMatch(normalizedQuestion, pattern.Regex, RegexOptions.IgnoreCase))
            {
                return pattern;
            }
        }

        return null;
    }

    public Dictionary<string, string> ExtractParameters(string question, QueryPattern pattern)
    {
        var parameters = new Dictionary<string, string>();
        var match = Regex.Match(question, pattern.Regex, RegexOptions.IgnoreCase);

        if (match.Success)
        {
            for (int i = 1; i < match.Groups.Count; i++)
            {
                parameters[$"param{i}"] = match.Groups[i].Value;
            }
        }

        return parameters;
    }
}

public class QueryPattern
{
    public required string Regex { get; set; }
    public required string Intent { get; set; }
    public required string Template { get; set; }
    public required string Example { get; set; }
}
