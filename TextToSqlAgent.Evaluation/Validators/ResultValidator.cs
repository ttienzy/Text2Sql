using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Evaluation.Models;

namespace TextToSqlAgent.Evaluation.Validators;

/// <summary>
/// P1-08: Validates query results against expected results
/// Ensures that success=true doesn't automatically mean correct results
/// </summary>
public class ResultValidator
{
    /// <summary>
    /// Validates if the actual query results match the expected results
    /// </summary>
    /// <param name="actual">Actual query execution result</param>
    /// <param name="expected">Expected results from evaluation example</param>
    /// <returns>True if results match, false otherwise</returns>
    public bool ValidateResults(
        SqlExecutionResult? actual,
        List<Dictionary<string, object>>? expected)
    {
        // If no expected results provided, we can't validate
        if (expected == null || expected.Count == 0)
        {
            return false;
        }

        // If query failed to execute, results don't match
        if (actual == null || !actual.Success)
        {
            return false;
        }

        // Check row count matches
        if (actual.Rows.Count != expected.Count)
        {
            return false;
        }

        // If both are empty, they match
        if (actual.Rows.Count == 0)
        {
            return true;
        }

        // Compare each row
        for (int i = 0; i < expected.Count; i++)
        {
            if (!CompareRows(actual.Rows[i], expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two rows for equality
    /// </summary>
    private bool CompareRows(
        Dictionary<string, object?> actualRow,
        Dictionary<string, object> expectedRow)
    {
        // Check if all expected keys exist in actual row
        foreach (var expectedKey in expectedRow.Keys)
        {
            // Case-insensitive key comparison (SQL column names)
            var actualKey = actualRow.Keys.FirstOrDefault(k =>
                k.Equals(expectedKey, StringComparison.OrdinalIgnoreCase));

            if (actualKey == null)
            {
                return false;
            }

            var actualValue = actualRow[actualKey];
            var expectedValue = expectedRow[expectedKey];

            if (!CompareValues(actualValue, expectedValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two values for equality with type coercion
    /// </summary>
    private bool CompareValues(object? actual, object? expected)
    {
        // Both null
        if (actual == null && expected == null)
        {
            return true;
        }

        // One null, one not
        if (actual == null || expected == null)
        {
            return false;
        }

        // Try direct equality first
        if (actual.Equals(expected))
        {
            return true;
        }

        // Handle numeric comparisons (int vs long, decimal vs double, etc.)
        if (IsNumeric(actual) && IsNumeric(expected))
        {
            return CompareNumeric(actual, expected);
        }

        // Handle string comparisons (case-insensitive)
        if (actual is string actualStr && expected is string expectedStr)
        {
            return actualStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase);
        }

        // Handle DateTime comparisons (ignore milliseconds)
        if (actual is DateTime actualDt && expected is DateTime expectedDt)
        {
            return Math.Abs((actualDt - expectedDt).TotalSeconds) < 1;
        }

        // Handle boolean comparisons
        if (actual is bool actualBool && expected is bool expectedBool)
        {
            return actualBool == expectedBool;
        }

        // Try string representation as last resort
        return actual.ToString() == expected.ToString();
    }

    /// <summary>
    /// Checks if a value is numeric
    /// </summary>
    private bool IsNumeric(object value)
    {
        return value is sbyte || value is byte ||
               value is short || value is ushort ||
               value is int || value is uint ||
               value is long || value is ulong ||
               value is float || value is double ||
               value is decimal;
    }

    /// <summary>
    /// Compares two numeric values
    /// </summary>
    private bool CompareNumeric(object actual, object expected)
    {
        try
        {
            var actualDecimal = Convert.ToDecimal(actual);
            var expectedDecimal = Convert.ToDecimal(expected);

            // Allow small floating point differences
            return Math.Abs(actualDecimal - expectedDecimal) < 0.0001m;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates result similarity score (0-100)
    /// Useful for partial credit in evaluation
    /// </summary>
    public double CalculateSimilarity(
        SqlExecutionResult? actual,
        List<Dictionary<string, object>>? expected)
    {
        if (expected == null || expected.Count == 0)
        {
            return 0;
        }

        if (actual == null || !actual.Success)
        {
            return 0;
        }

        // If exact match, return 100
        if (ValidateResults(actual, expected))
        {
            return 100;
        }

        // Calculate partial similarity
        double score = 0;

        // Row count similarity (max 30 points)
        if (expected.Count > 0)
        {
            var rowCountRatio = Math.Min(actual.Rows.Count, expected.Count) /
                               (double)Math.Max(actual.Rows.Count, expected.Count);
            score += rowCountRatio * 30;
        }

        // Column similarity (max 20 points)
        if (actual.Rows.Count > 0 && expected.Count > 0)
        {
            var actualColumns = actual.Rows[0].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expectedColumns = expected[0].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var commonColumns = actualColumns.Intersect(expectedColumns, StringComparer.OrdinalIgnoreCase).Count();
            var totalColumns = actualColumns.Union(expectedColumns, StringComparer.OrdinalIgnoreCase).Count();

            if (totalColumns > 0)
            {
                score += (commonColumns / (double)totalColumns) * 20;
            }
        }

        // Value similarity (max 50 points)
        int matchingRows = 0;
        int maxRowsToCompare = Math.Min(actual.Rows.Count, expected.Count);

        for (int i = 0; i < maxRowsToCompare; i++)
        {
            if (CompareRows(actual.Rows[i], expected[i]))
            {
                matchingRows++;
            }
        }

        if (maxRowsToCompare > 0)
        {
            score += (matchingRows / (double)maxRowsToCompare) * 50;
        }

        return score;
    }
}
