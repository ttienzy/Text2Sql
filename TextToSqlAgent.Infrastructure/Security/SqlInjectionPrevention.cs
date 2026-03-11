using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace TextToSqlAgent.Infrastructure.Security;

/// <summary>
/// SQL injection prevention and input validation
/// </summary>
public class SqlInjectionPrevention
{
    private readonly ILogger<SqlInjectionPrevention> _logger;

    // Dangerous SQL patterns
    private static readonly string[] DangerousPatterns = new[]
    {
        @";\s*(DROP|DELETE|TRUNCATE|ALTER|CREATE|EXEC|EXECUTE)\s+",
        @"--",
        @"/\*.*\*/",
        @"xp_cmdshell",
        @"sp_executesql",
        @"UNION\s+SELECT",
        @"INTO\s+OUTFILE",
        @"LOAD_FILE",
        @"@@version",
        @"information_schema",
        @"sys\.",
        @"master\.",
        @"msdb\."
    };

    // Allowed SQL keywords for generated queries
    private static readonly HashSet<string> AllowedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER",
        "ON", "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "IS", "NULL",
        "GROUP", "BY", "HAVING", "ORDER", "ASC", "DESC", "LIMIT", "OFFSET",
        "COUNT", "SUM", "AVG", "MIN", "MAX", "DISTINCT", "AS",
        "CASE", "WHEN", "THEN", "ELSE", "END", "WITH", "CTE"
    };

    public SqlInjectionPrevention(ILogger<SqlInjectionPrevention> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate SQL query for injection attempts
    /// </summary>
    public ValidationResult ValidateSql(string sql)
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(sql))
        {
            result.IsValid = false;
            result.Errors.Add("SQL query is empty");
            return result;
        }

        // Check for dangerous patterns
        foreach (var pattern in DangerousPatterns)
        {
            if (Regex.IsMatch(sql, pattern, RegexOptions.IgnoreCase))
            {
                result.IsValid = false;
                result.Errors.Add($"Dangerous SQL pattern detected: {pattern}");
                _logger.LogWarning("SQL injection attempt detected: {Pattern} in query: {Sql}", pattern, sql);
            }
        }

        // Check for multiple statements (should only have one SELECT)
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (statements.Length > 1)
        {
            result.IsValid = false;
            result.Errors.Add("Multiple SQL statements not allowed");
            _logger.LogWarning("Multiple SQL statements detected in query: {Sql}", sql);
        }

        // Check if query starts with SELECT
        var trimmed = sql.Trim();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            result.IsValid = false;
            result.Errors.Add("Query must start with SELECT or WITH");
            _logger.LogWarning("Query does not start with SELECT or WITH: {Sql}", sql);
        }

        // Check for disallowed keywords
        var upperSql = sql.ToUpper();
        var disallowedKeywords = new[] { "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE", "EXEC", "EXECUTE" };
        foreach (var keyword in disallowedKeywords)
        {
            if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
            {
                result.IsValid = false;
                result.Errors.Add($"Disallowed keyword: {keyword}");
                _logger.LogWarning("Disallowed keyword {Keyword} in query: {Sql}", keyword, sql);
            }
        }

        return result;
    }

    /// <summary>
    /// Sanitize user input for use in SQL
    /// </summary>
    public string SanitizeInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Remove dangerous characters
        var sanitized = input
            .Replace("'", "''")  // Escape single quotes
            .Replace("--", "")   // Remove SQL comments
            .Replace("/*", "")   // Remove block comments
            .Replace("*/", "")
            .Replace(";", "");   // Remove statement terminators

        return sanitized;
    }

    /// <summary>
    /// Validate table/column identifier
    /// </summary>
    public bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // Identifier should only contain alphanumeric, underscore, and dot
        return Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_\.]*$");
    }

    /// <summary>
    /// Escape identifier for safe use in SQL
    /// </summary>
    public string EscapeIdentifier(string identifier)
    {
        if (!IsValidIdentifier(identifier))
        {
            throw new ArgumentException($"Invalid identifier: {identifier}");
        }

        // Use square brackets for SQL Server
        return $"[{identifier.Replace("]", "]]")}]";
    }

    /// <summary>
    /// Validate question input
    /// </summary>
    public ValidationResult ValidateQuestion(string question)
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(question))
        {
            result.IsValid = false;
            result.Errors.Add("Question is empty");
            return result;
        }

        if (question.Length > 1000)
        {
            result.IsValid = false;
            result.Errors.Add("Question is too long (max 1000 characters)");
        }

        // Check for SQL injection attempts in question
        foreach (var pattern in DangerousPatterns)
        {
            if (Regex.IsMatch(question, pattern, RegexOptions.IgnoreCase))
            {
                result.IsValid = false;
                result.Errors.Add("Suspicious pattern detected in question");
                _logger.LogWarning("Suspicious pattern in question: {Pattern}", pattern);
            }
        }

        return result;
    }
}

/// <summary>
/// Validation result
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();

    public override string ToString()
    {
        if (IsValid)
            return "✓ Valid";

        return $"✗ Invalid:\n  - {string.Join("\n  - ", Errors)}";
    }
}
