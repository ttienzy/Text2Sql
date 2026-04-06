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

        // Check for UNION-based injection
        if (UnionInjectionPattern.IsMatch(trimmedSql))
        {
            return new SqlSafetyResult
            {
                IsSafe = false,
                ErrorMessage = "UNION-based queries are not allowed for security reasons. Please rephrase your question.",
                BlockedCommand = "UNION SELECT"
            };
        }

        // Check for OR-based tautology injection patterns (e.g., OR 1=1, OR 'a'='a')
        if (OrInjectionPattern.IsMatch(trimmedSql))
        {
            return new SqlSafetyResult
            {
                IsSafe = false,
                ErrorMessage = "Suspicious OR pattern detected (possible SQL injection). Query blocked for safety.",
                BlockedCommand = "OR tautology"
            };
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

    // ============================================================
    // CRIT-2b: Schema whitelist validation
    // Validates that table/column names in generated SQL actually
    // exist in the loaded database schema.
    // ============================================================

    // Regex to extract table names from FROM and JOIN clauses
    private static readonly Regex TableNamePattern = new(
        @"(?:FROM|JOIN)\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)(?:\s+(?:AS\s+)?(\w+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// CRIT-2b: Validates that all table names referenced in the SQL exist in the schema.
    /// Should be called after LLM generates SQL to prevent referencing non-existent tables.
    /// </summary>
    /// <param name="sql">The generated SQL query.</param>
    /// <param name="knownTables">Set of valid table names from the loaded schema (case-insensitive).</param>
    /// <param name="knownColumns">Optional set of valid column names (format: "TableName.ColumnName").</param>
    /// <returns>Validation result.</returns>
    public SqlSafetyResult ValidateAgainstSchema(
        string sql,
        IReadOnlySet<string> knownTables,
        IReadOnlySet<string>? knownColumns = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new SqlSafetyResult { IsSafe = false, ErrorMessage = "SQL query cannot be empty" };

        // Extract table names from SQL
        var matches = TableNamePattern.Matches(sql);
        var unknownTables = new List<string>();

        foreach (Match match in matches)
        {
            // Group 2 is the table name (Group 1 is optional schema prefix)
            var tableName = match.Groups[2].Value;
            if (string.IsNullOrEmpty(tableName)) continue;

            // Skip INFORMATION_SCHEMA and sys tables (they are valid SQL Server system tables)
            if (tableName.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase) ||
                tableName.Equals("sys", StringComparison.OrdinalIgnoreCase))
                continue;

            // Check against known tables (case-insensitive)
            if (!knownTables.Contains(tableName))
            {
                unknownTables.Add(tableName);
            }
        }

        if (unknownTables.Count > 0)
        {
            return new SqlSafetyResult
            {
                IsSafe = false,
                ErrorMessage = $"SQL references unknown table(s): {string.Join(", ", unknownTables)}. " +
                               "These tables do not exist in the database schema.",
                BlockedCommand = "Unknown table reference"
            };
        }

        return new SqlSafetyResult { IsSafe = true };
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

