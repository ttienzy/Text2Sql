using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Models;
using System.Text.RegularExpressions;

namespace TextToSqlAgent.Application.Services.Validation;

/// <summary>
/// Multi-level SQL validation: Syntax → Semantic → Result validation
/// Inspired by SQLFixAgent paper (2024)
/// </summary>
public class SemanticSqlValidator
{
    private readonly ILogger<SemanticSqlValidator> _logger;
    private readonly Kernel? _kernel;

    public SemanticSqlValidator(
        ILogger<SemanticSqlValidator> logger,
        Kernel? kernel = null)
    {
        _logger = logger;
        _kernel = kernel;
    }

    /// <summary>
    /// Level 1: Syntax validation (basic structure checks)
    /// </summary>
    public ValidationResult ValidateSyntax(string sql)
    {
        _logger.LogDebug("[SemanticValidator] Validating syntax for SQL");

        var errors = new List<string>();

        // Check for forbidden keywords
        var forbiddenKeywords = new[]
        {
            "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE", "ALTER",
            "EXEC", "EXECUTE", "xp_", "sp_",
            "CREATE", "GRANT", "REVOKE", "DENY",
            "BACKUP", "RESTORE", "SHUTDOWN"
        };

        foreach (var keyword in forbiddenKeywords)
        {
            if (Regex.IsMatch(sql, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
            {
                errors.Add($"Forbidden keyword detected: {keyword}");
            }
        }

        // Check basic structure
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Query must start with SELECT or WITH (CTE)");
        }

        // Check for balanced parentheses
        var openCount = sql.Count(c => c == '(');
        var closeCount = sql.Count(c => c == ')');
        if (openCount != closeCount)
        {
            errors.Add($"Unbalanced parentheses: {openCount} open, {closeCount} close");
        }

        // Check for balanced brackets
        var openBracketCount = sql.Count(c => c == '[');
        var closeBracketCount = sql.Count(c => c == ']');
        if (openBracketCount != closeBracketCount)
        {
            errors.Add($"Unbalanced brackets: {openBracketCount} open, {closeBracketCount} close");
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Fail(string.Join("; ", errors));
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Level 2: Semantic validation (schema-aware checks)
    /// </summary>
    public ValidationResult ValidateSemantic(
        string sql,
        string originalQuery,
        DatabaseSchema schema)
    {
        _logger.LogDebug("[SemanticValidator] Validating semantic correctness");

        var errors = new List<string>();

        // 1. Check table existence
        var tables = ExtractTablesFromSql(sql);
        var missingTables = tables
            .Where(t => !schema.Tables.Any(st =>
                st.TableName.Equals(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingTables.Count > 0)
        {
            errors.Add($"Tables not found in schema: {string.Join(", ", missingTables)}");
        }

        // 2. Verify JOIN conditions make sense
        var joins = ExtractJoins(sql);
        foreach (var join in joins)
        {
            if (!IsValidJoinCondition(join, schema))
            {
                errors.Add($"Invalid JOIN condition: {join}");
            }
        }

        // 3. Check GROUP BY consistency
        if (HasGroupBy(sql) && !ValidateGroupByColumns(sql))
        {
            errors.Add("GROUP BY columns mismatch with SELECT clause");
        }

        // 4. Check for common anti-patterns
        if (sql.Contains("SELECT *") && tables.Count > 1)
        {
            errors.Add("SELECT * with multiple tables may cause ambiguity");
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Fail(string.Join("; ", errors));
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Level 3: LLM-based "rubber duck debugging"
    /// Ask LLM to review the SQL for semantic correctness
    /// </summary>
    public async Task<ValidationResult> ValidateWithLlmAsync(
        string sql,
        string originalQuery,
        DatabaseSchema schema)
    {
        _logger.LogDebug("[SemanticValidator] Performing LLM-based validation");

        if (_kernel == null)
        {
            _logger.LogWarning("[SemanticValidator] Kernel not available, skipping LLM validation");
            return ValidationResult.Success();
        }

        try
        {
            var schemaContext = FormatSchemaForValidation(schema);

            var prompt = $@"You are a SQL expert reviewer. Analyze this SQL query for semantic correctness.

User Question: {originalQuery}

Generated SQL:
{sql}

Database Schema:
{schemaContext}

Review the SQL and check for:
1. Does it correctly answer the user's question?
2. Are there any logical errors (wrong JOINs, incorrect filters)?
3. Are there any ambiguities or edge cases?
4. Is the query optimal or are there better approaches?

Respond with JSON:
{{
  ""is_valid"": true/false,
  ""confidence"": 0.0-1.0,
  ""issues"": [""issue1"", ""issue2""],
  ""suggestions"": [""suggestion1"", ""suggestion2""]
}}

If the SQL is correct, return is_valid: true with high confidence.
If there are issues, explain them clearly in the issues array.";

            var response = await _kernel.InvokePromptAsync(prompt);
            var result = response.ToString();

            // Parse JSON response
            var validation = ParseLlmValidationResponse(result);

            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "[SemanticValidator] LLM detected issues: {Issues}",
                    string.Join("; ", validation.Issues));

                return ValidationResult.Fail(
                    $"LLM validation failed: {string.Join("; ", validation.Issues)}",
                    validation.Suggestions);
            }

            if (validation.Confidence < 0.7)
            {
                _logger.LogWarning(
                    "[SemanticValidator] Low confidence: {Confidence}",
                    validation.Confidence);

                return ValidationResult.Warn(
                    $"Low confidence ({validation.Confidence:F2})",
                    validation.Suggestions);
            }

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SemanticValidator] LLM validation failed");
            return ValidationResult.Warn("LLM validation unavailable");
        }
    }

    /// <summary>
    /// Level 4: Result validation (check query results for anomalies)
    /// </summary>
    public ValidationResult ValidateResult(
        SqlExecutionResult result,
        string originalQuery,
        string sql)
    {
        _logger.LogDebug("[SemanticValidator] Validating query results");

        var warnings = new List<string>();

        // 1. Check for excessive nulls (may indicate JOIN issue)
        if (result.Rows?.Count > 0)
        {
            var nullPercentage = CalculateNullPercentage(result.Rows);
            if (nullPercentage > 0.5)
            {
                warnings.Add($"High null percentage ({nullPercentage:P0}) - possible JOIN issue");
            }
        }

        // 2. Check for negative amounts in financial queries
        if (IsFinancialQuery(originalQuery) && HasNegativeAmounts(result))
        {
            warnings.Add("Negative amounts detected in financial query");
        }

        // 3. Check for suspiciously large numbers
        if (HasSuspiciouslyLargeNumbers(result))
        {
            warnings.Add("Suspiciously large numbers detected - verify calculations");
        }

        // 4. Check for empty result when data expected
        if (result.Rows?.Count == 0 && !IsCountQuery(sql))
        {
            warnings.Add("Empty result - verify filters and JOIN conditions");
        }

        if (warnings.Count > 0)
        {
            return ValidationResult.Warn(string.Join("; ", warnings));
        }

        return ValidationResult.Success();
    }

    #region Helper Methods

    private List<string> ExtractTablesFromSql(string sql)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Match FROM and JOIN clauses
        var patterns = new[]
        {
            @"FROM\s+\[?(\w+)\]?",
            @"JOIN\s+\[?(\w+)\]?"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    tables.Add(match.Groups[1].Value);
                }
            }
        }

        return tables.ToList();
    }

    private List<string> ExtractJoins(string sql)
    {
        var joins = new List<string>();

        var pattern = @"(INNER|LEFT|RIGHT|FULL)?\s*JOIN\s+\[?\w+\]?\s+(?:AS\s+)?\w*\s+ON\s+([^WHERE|GROUP|ORDER|;]+)";
        var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 2)
            {
                joins.Add(match.Groups[2].Value.Trim());
            }
        }

        return joins;
    }

    private bool IsValidJoinCondition(string joinCondition, DatabaseSchema schema)
    {
        // Basic validation: check if JOIN condition has = operator
        if (!joinCondition.Contains("="))
        {
            return false;
        }

        // TODO: More sophisticated validation
        // - Check if columns exist in respective tables
        // - Check if data types are compatible
        // - Check if it's a valid FK relationship

        return true;
    }

    private bool HasGroupBy(string sql)
    {
        return Regex.IsMatch(sql, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase);
    }

    private bool ValidateGroupByColumns(string sql)
    {
        // TODO: Implement proper GROUP BY validation
        // - Extract SELECT columns
        // - Extract GROUP BY columns
        // - Verify non-aggregated SELECT columns are in GROUP BY

        return true;
    }

    private string FormatSchemaForValidation(DatabaseSchema schema)
    {
        var tables = schema.Tables.Take(10).Select(t =>
            $"{t.TableName}: {string.Join(", ", t.Columns.Take(5).Select(c => c.ColumnName))}");

        return string.Join("\n", tables);
    }

    private LlmValidationResult ParseLlmValidationResponse(string response)
    {
        try
        {
            // Simple JSON parsing (in production, use System.Text.Json)
            var isValid = response.Contains("\"is_valid\": true");
            var confidence = 0.8; // Default

            var confidenceMatch = Regex.Match(response, @"""confidence"":\s*([\d.]+)");
            if (confidenceMatch.Success)
            {
                double.TryParse(confidenceMatch.Groups[1].Value, out confidence);
            }

            var issues = new List<string>();
            var issuesMatch = Regex.Match(response, @"""issues"":\s*\[(.*?)\]", RegexOptions.Singleline);
            if (issuesMatch.Success)
            {
                var issuesStr = issuesMatch.Groups[1].Value;
                var issueMatches = Regex.Matches(issuesStr, @"""([^""]+)""");
                foreach (Match match in issueMatches)
                {
                    issues.Add(match.Groups[1].Value);
                }
            }

            var suggestions = new List<string>();
            var suggestionsMatch = Regex.Match(response, @"""suggestions"":\s*\[(.*?)\]", RegexOptions.Singleline);
            if (suggestionsMatch.Success)
            {
                var suggestionsStr = suggestionsMatch.Groups[1].Value;
                var suggestionMatches = Regex.Matches(suggestionsStr, @"""([^""]+)""");
                foreach (Match match in suggestionMatches)
                {
                    suggestions.Add(match.Groups[1].Value);
                }
            }

            return new LlmValidationResult
            {
                IsValid = isValid,
                Confidence = confidence,
                Issues = issues,
                Suggestions = suggestions
            };
        }
        catch (Exception)
        {
            return new LlmValidationResult
            {
                IsValid = true,
                Confidence = 0.5,
                Issues = [],
                Suggestions = []
            };
        }
    }

    private double CalculateNullPercentage(List<Dictionary<string, object>> rows)
    {
        if (rows.Count == 0) return 0;

        var totalCells = rows.Count * rows[0].Count;
        var nullCells = rows.Sum(row => row.Values.Count(v => v == null || v == DBNull.Value));

        return (double)nullCells / totalCells;
    }

    private bool IsFinancialQuery(string query)
    {
        var financialKeywords = new[] { "price", "amount", "revenue", "cost", "total", "sum" };
        return financialKeywords.Any(k => query.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasNegativeAmounts(SqlExecutionResult result)
    {
        if (result.Rows == null) return false;

        foreach (var row in result.Rows)
        {
            foreach (var value in row.Values)
            {
                if (value is decimal d && d < 0) return true;
                if (value is double db && db < 0) return true;
                if (value is int i && i < 0) return true;
            }
        }

        return false;
    }

    private bool HasSuspiciouslyLargeNumbers(SqlExecutionResult result)
    {
        if (result.Rows == null) return false;

        const long threshold = 1_000_000_000_000; // 1 trillion

        foreach (var row in result.Rows)
        {
            foreach (var value in row.Values)
            {
                if (value is decimal d && Math.Abs(d) > threshold) return true;
                if (value is double db && Math.Abs(db) > threshold) return true;
                if (value is long l && Math.Abs(l) > threshold) return true;
            }
        }

        return false;
    }

    private bool IsCountQuery(string sql)
    {
        return Regex.IsMatch(sql, @"\bCOUNT\s*\(", RegexOptions.IgnoreCase);
    }

    #endregion
}

/// <summary>
/// Validation result with severity levels
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public ValidationSeverity Severity { get; set; }
    public string? Message { get; set; }
    public List<string> Suggestions { get; set; } = [];

    public static ValidationResult Success() => new()
    {
        IsValid = true,
        Severity = ValidationSeverity.None
    };

    public static ValidationResult Fail(string message, List<string>? suggestions = null) => new()
    {
        IsValid = false,
        Severity = ValidationSeverity.Error,
        Message = message,
        Suggestions = suggestions ?? []
    };

    public static ValidationResult Warn(string message, List<string>? suggestions = null) => new()
    {
        IsValid = true,
        Severity = ValidationSeverity.Warning,
        Message = message,
        Suggestions = suggestions ?? []
    };
}

public enum ValidationSeverity
{
    None,
    Warning,
    Error
}

/// <summary>
/// LLM validation response
/// </summary>
internal class LlmValidationResult
{
    public bool IsValid { get; set; }
    public double Confidence { get; set; }
    public List<string> Issues { get; set; } = [];
    public List<string> Suggestions { get; set; } = [];
}
