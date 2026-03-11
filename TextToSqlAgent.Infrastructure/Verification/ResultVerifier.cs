using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Verification;

/// <summary>
/// Verifies if SQL execution results make sense for the given question
/// </summary>
public class ResultVerifier
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<ResultVerifier> _logger;

    public ResultVerifier(
        ILLMClient llmClient,
        ILogger<ResultVerifier> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    /// <summary>
    /// Verify if SQL result makes sense for the question
    /// </summary>
    public async Task<VerificationResult> VerifyAsync(
        string question,
        string sql,
        SqlExecutionResult executionResult,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Verifying result for question: {Question}", question);

        // Quick sanity checks first
        var sanityCheck = PerformSanityChecks(question, sql, executionResult);
        if (!sanityCheck.IsValid)
        {
            return sanityCheck;
        }

        // LLM-based verification for deeper analysis
        try
        {
            var llmVerification = await VerifyWithLLMAsync(question, sql, executionResult, ct);
            return llmVerification;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM verification failed, falling back to sanity check");
            return sanityCheck;
        }
    }

    /// <summary>
    /// Perform quick sanity checks without LLM
    /// </summary>
    private VerificationResult PerformSanityChecks(
        string question,
        string sql,
        SqlExecutionResult executionResult)
    {
        var issues = new List<string>();
        var confidence = 1.0;

        // Check 1: Execution success
        if (!executionResult.Success)
        {
            return new VerificationResult
            {
                IsValid = false,
                Confidence = 0.0,
                Issues = new List<string> { $"SQL execution failed: {executionResult.ErrorMessage}" },
                Suggestion = "Fix SQL syntax or logic errors"
            };
        }

        // Check 2: Empty result for counting/aggregation queries
        if (executionResult.RowCount == 0)
        {
            var lowerSql = sql.ToLower();
            var lowerQuestion = question.ToLower();

            // If asking for count/sum/avg but got 0 rows, might be suspicious
            if ((lowerQuestion.Contains("how many") || lowerQuestion.Contains("count") ||
                 lowerQuestion.Contains("total") || lowerQuestion.Contains("sum")) &&
                (lowerSql.Contains("count(") || lowerSql.Contains("sum(") || lowerSql.Contains("avg(")))
            {
                issues.Add("Query returned 0 rows for aggregation - might indicate no matching data");
                confidence -= 0.2;
            }
        }

        // Check 3: Null results
        if (executionResult.Rows != null && executionResult.Rows.Count > 0)
        {
            var firstRow = executionResult.Rows[0];
            var nullCount = firstRow.Values.Count(v => v == null);
            var totalColumns = firstRow.Count;

            if (nullCount > 0 && nullCount == totalColumns)
            {
                issues.Add("All columns are NULL in result");
                confidence -= 0.3;
            }
            else if (nullCount > totalColumns / 2)
            {
                issues.Add($"Many NULL values ({nullCount}/{totalColumns} columns)");
                confidence -= 0.1;
            }
        }

        // Check 4: Unreasonable row counts
        if (executionResult.RowCount > 10000)
        {
            var lowerQuestion = question.ToLower();
            if (lowerQuestion.Contains("top") || lowerQuestion.Contains("first") ||
                lowerQuestion.Contains("limit") || lowerQuestion.Contains("few"))
            {
                issues.Add($"Query returned {executionResult.RowCount} rows but question suggests limited results");
                confidence -= 0.2;
            }
        }

        // Check 5: Single row for "list" or "all" queries
        if (executionResult.RowCount == 1)
        {
            var lowerQuestion = question.ToLower();
            if (lowerQuestion.Contains("list") || lowerQuestion.Contains("all") ||
                lowerQuestion.Contains("show me") || lowerQuestion.Contains("what are"))
            {
                issues.Add("Query returned only 1 row but question suggests multiple results");
                confidence -= 0.15;
            }
        }

        return new VerificationResult
        {
            IsValid = confidence > 0.5,
            Confidence = Math.Max(0, confidence),
            Issues = issues,
            Suggestion = issues.Count > 0
                ? "Review SQL logic and data availability"
                : "Result looks reasonable"
        };
    }

    /// <summary>
    /// Use LLM to verify result makes sense
    /// </summary>
    private async Task<VerificationResult> VerifyWithLLMAsync(
        string question,
        string sql,
        SqlExecutionResult executionResult,
        CancellationToken ct)
    {
        var resultSummary = FormatResultSummary(executionResult);

        var prompt = $@"Verify if SQL result makes sense for the question.

Question: {question}

SQL Query:
{sql}

Result Summary:
{resultSummary}

Analyze:
1. Does the result answer the question?
2. Are the values reasonable?
3. Is the row count expected?
4. Are there any suspicious patterns?

Return JSON:
{{
  ""is_valid"": true/false,
  ""confidence"": 0.0-1.0,
  ""issues"": [""issue1"", ""issue2""],
  ""suggestion"": ""what to fix or improve""
}}

Be concise and specific.";

        var response = await _llmClient.CompleteAsync(prompt, ct);
        return ParseVerificationResponse(response);
    }

    /// <summary>
    /// Format result summary for LLM
    /// </summary>
    private string FormatResultSummary(SqlExecutionResult result)
    {
        if (!result.Success)
        {
            return $"Error: {result.ErrorMessage}";
        }

        var summary = $"Row Count: {result.RowCount}\n";
        summary += $"Execution Time: {result.ExecutionTimeMs}ms\n";

        if (result.Rows != null && result.Rows.Count > 0)
        {
            summary += "\nSample Data (first 3 rows):\n";
            var sampleRows = result.Rows.Take(3);

            foreach (var row in sampleRows)
            {
                var rowStr = string.Join(", ", row.Select(kvp => $"{kvp.Key}={kvp.Value ?? "NULL"}"));
                summary += $"  {rowStr}\n";
            }

            if (result.RowCount > 3)
            {
                summary += $"  ... and {result.RowCount - 3} more rows\n";
            }
        }
        else
        {
            summary += "No data returned\n";
        }

        return summary;
    }

    /// <summary>
    /// Parse LLM verification response
    /// </summary>
    private VerificationResult ParseVerificationResponse(string response)
    {
        try
        {
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var result = System.Text.Json.JsonSerializer.Deserialize<VerificationResult>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (result != null)
                {
                    return result;
                }
            }

            // Fallback: parse manually
            return new VerificationResult
            {
                IsValid = !response.ToLower().Contains("false") && !response.ToLower().Contains("invalid"),
                Confidence = 0.7,
                Issues = new List<string> { "Could not parse LLM response fully" },
                Suggestion = "Manual review recommended"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse verification response");
            return new VerificationResult
            {
                IsValid = true,
                Confidence = 0.5,
                Issues = new List<string> { "Verification parsing failed" },
                Suggestion = "Manual review recommended"
            };
        }
    }
}

/// <summary>
/// Result of verification
/// </summary>
public class VerificationResult
{
    public bool IsValid { get; set; }
    public double Confidence { get; set; }
    public List<string> Issues { get; set; } = new();
    public string Suggestion { get; set; } = string.Empty;

    public override string ToString()
    {
        var status = IsValid ? "✓ VALID" : "✗ INVALID";
        var result = $"{status} (Confidence: {Confidence:P0})\n";

        if (Issues.Count > 0)
        {
            result += "Issues:\n";
            foreach (var issue in Issues)
            {
                result += $"  - {issue}\n";
            }
        }

        if (!string.IsNullOrEmpty(Suggestion))
        {
            result += $"Suggestion: {Suggestion}\n";
        }

        return result;
    }
}
