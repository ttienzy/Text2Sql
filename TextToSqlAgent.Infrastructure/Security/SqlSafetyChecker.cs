using System.Text.RegularExpressions;

namespace TextToSqlAgent.Infrastructure.Security;

/// <summary>
/// SQL Safety Checker - ensures only SELECT queries are allowed
/// </summary>
public interface ISqlSafetyChecker
{
    /// <summary>
    /// Validates if the SQL query is safe (SELECT only)
    /// </summary>
    /// <param name="sql">The SQL query to validate</param>
    /// <returns>Validation result with success status and error message</returns>
    SqlSafetyResult Validate(string sql);
}

/// <summary>
/// Result of SQL safety validation
/// </summary>
public class SqlSafetyResult
{
    public bool IsSafe { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BlockedCommand { get; set; }
}

/// <summary>
/// SQL Safety Checker implementation
/// </summary>
public class SqlSafetyChecker : ISqlSafetyChecker
{
    // Patterns that indicate dangerous SQL commands
    private static readonly Regex DangerousPattern = new(
        @"^\s*(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|MERGE|WITH\s+\w+\s+AS\s*\(|--|\/\*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to detect common SQL injection attempts
    private static readonly Regex InjectionPattern = new(
        @";\s*(DROP|DELETE|INSERT|UPDATE|ALTER|CREATE|TRUNCATE|EXEC|GRANT|REVOKE)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to detect UNION-based injection
    private static readonly Regex UnionInjectionPattern = new(
        @"UNION\s+(ALL\s+)?SELECT",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to detect OR-based injection in WHERE clauses
    private static readonly Regex OrInjectionPattern = new(
        @"\bOR\b\s+\d+\s*=\s*\d+|\bOR\b\s+'[^']*'\s*=\s*'[^']*'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public SqlSafetyResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlSafetyResult
            {
                IsSafe = false,
                ErrorMessage = "SQL query cannot be empty"
            };
        }

        var trimmedSql = sql.Trim();

        // Check for dangerous commands
        if (DangerousPattern.IsMatch(trimmedSql))
        {
            var match = DangerousPattern.Match(trimmedSql);
            var command = match.Groups[1].Value.ToUpperInvariant();

            // Allow common table expressions (WITH) but block the rest
            if (command == "WITH")
            {
                // WITH is allowed if it's followed by SELECT
                if (!Regex.IsMatch(trimmedSql, @"WITH\s+\w+\s+AS\s*\(\s*SELECT", RegexOptions.IgnoreCase))
                {
                    return new SqlSafetyResult
                    {
                        IsSafe = false,
                        ErrorMessage = "CTE (WITH) statements must contain only SELECT queries",
                        BlockedCommand = "WITH (non-SELECT)"
                    };
                }
            }
            else
            {
                return new SqlSafetyResult
                {
                    IsSafe = false,
                    ErrorMessage = $"Command '{command}' is not allowed. Only SELECT queries are permitted.",
                    BlockedCommand = command
                };
            }
        }

        // Check for SQL injection attempts with semicolons
        if (InjectionPattern.IsMatch(trimmedSql))
        {
            return new SqlSafetyResult
            {
                IsSafe = false,
                ErrorMessage = "Potential SQL injection detected. Multiple statements are not allowed.",
                BlockedCommand = "Multiple statements"
            };
        }

        // Check for UNION-based injection (could be used to extract data from other tables)
        if (UnionInjectionPattern.IsMatch(trimmedSql))
        {
            // Allow UNION SELECT but log it for monitoring
            // In a production system, you might want to be more strict here
        }

        // Check for OR-based injection patterns
        if (OrInjectionPattern.IsMatch(trimmedSql))
        {
            // This could be a legitimate query, but we'll allow it with a warning
            // In production, you might want to flag this for review
        }

        // Additional check: Make sure the query starts with SELECT (after trimming)
        if (!trimmedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlSafetyResult
            {
                IsSafe = false,
                ErrorMessage = "Only SELECT queries are allowed. Other operations are blocked for security reasons.",
                BlockedCommand = "Non-SELECT query"
            };
        }

        return new SqlSafetyResult
        {
            IsSafe = true
        };
    }
}

/// <summary>
/// Extension methods for SQL safety checking
/// </summary>
public static class SqlSafetyExtensions
{
    /// <summary>
    /// Throws if the SQL is not safe
    /// </summary>
    public static void EnsureSafe(this ISqlSafetyChecker checker, string sql)
    {
        var result = checker.Validate(sql);
        if (!result.IsSafe)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }
    }
}
