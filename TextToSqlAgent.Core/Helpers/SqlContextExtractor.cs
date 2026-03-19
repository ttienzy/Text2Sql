using System.Text.RegularExpressions;

namespace TextToSqlAgent.Core.Helpers;

/// <summary>
/// Extracts structured context from SQL queries for conversation memory
/// </summary>
public static class SqlContextExtractor
{
    /// <summary>
    /// Extract all tables referenced in SQL query
    /// </summary>
    public static List<string> ExtractTables(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new List<string>();

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // FROM clause: FROM [TableName] or FROM TableName
        var fromMatches = Regex.Matches(
            sql,
            @"FROM\s+\[?(\w+)\]?",
            RegexOptions.IgnoreCase);

        foreach (Match match in fromMatches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                tables.Add(match.Groups[1].Value);
            }
        }

        // JOIN clauses: JOIN [TableName] or JOIN TableName
        var joinMatches = Regex.Matches(
            sql,
            @"JOIN\s+\[?(\w+)\]?",
            RegexOptions.IgnoreCase);

        foreach (Match match in joinMatches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                tables.Add(match.Groups[1].Value);
            }
        }

        return tables.ToList();
    }

    /// <summary>
    /// Extract primary table (first table in FROM clause)
    /// </summary>
    public static string? ExtractPrimaryTable(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        var match = Regex.Match(
            sql,
            @"FROM\s+\[?(\w+)\]?",
            RegexOptions.IgnoreCase);

        return match.Success && match.Groups.Count > 1
            ? match.Groups[1].Value
            : null;
    }

    /// <summary>
    /// Extract columns from SELECT clause
    /// </summary>
    public static Dictionary<string, string> ExtractColumns(string sql)
    {
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sql))
            return columns;

        // Find SELECT ... FROM
        var selectMatch = Regex.Match(
            sql,
            @"SELECT\s+(.*?)\s+FROM",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!selectMatch.Success || selectMatch.Groups.Count < 2)
            return columns;

        var selectClause = selectMatch.Groups[1].Value;

        // Handle SELECT *
        if (selectClause.Trim() == "*")
        {
            columns["*"] = "*";
            return columns;
        }

        // Split by comma (simple approach)
        var parts = selectClause.Split(',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            // Handle: ColumnName AS Alias or ColumnName Alias
            var aliasMatch = Regex.Match(trimmed, @"(.+?)\s+(?:AS\s+)?(\w+)$", RegexOptions.IgnoreCase);

            if (aliasMatch.Success && aliasMatch.Groups.Count > 2)
            {
                var column = aliasMatch.Groups[1].Value.Trim();
                var alias = aliasMatch.Groups[2].Value.Trim();
                columns[alias] = column;
            }
            else
            {
                // No alias, use column name as both key and value
                var cleanColumn = trimmed.Replace("[", "").Replace("]", "");
                columns[cleanColumn] = cleanColumn;
            }
        }

        return columns;
    }

    /// <summary>
    /// Detect query intent type from SQL
    /// </summary>
    public static string DetectIntentType(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "UNKNOWN";

        var upperSql = sql.ToUpperInvariant();

        // COUNT queries
        if (upperSql.Contains("COUNT("))
            return "COUNT";

        // Aggregate queries
        if (upperSql.Contains("SUM(") || upperSql.Contains("AVG(") ||
            upperSql.Contains("MAX(") || upperSql.Contains("MIN("))
            return "AGGREGATE";

        // GROUP BY queries
        if (upperSql.Contains("GROUP BY"))
            return "GROUP_BY";

        // TOP N queries
        if (upperSql.Contains("TOP ") || upperSql.Contains("LIMIT "))
            return "TOP_N";

        // ORDER BY (ranking)
        if (upperSql.Contains("ORDER BY"))
            return "RANKING";

        // Default: LIST
        return "LIST";
    }

    /// <summary>
    /// Extract full context from SQL query
    /// </summary>
    public static (List<string> Tables, string? PrimaryTable, Dictionary<string, string> Columns, string IntentType) ExtractFullContext(string sql)
    {
        return (
            ExtractTables(sql),
            ExtractPrimaryTable(sql),
            ExtractColumns(sql),
            DetectIntentType(sql)
        );
    }
}
