using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Models;
using System.Text.Json;

namespace TextToSqlAgent.Application.Services.Correction;

/// <summary>
/// Multi-agent SQL correction system inspired by MAGIC paper (2024)
/// Uses Manager → Corrector → Reviewer architecture for iterative SQL fixing
/// </summary>
public class MultiAgentCorrectionSystem
{
    private readonly ILogger<MultiAgentCorrectionSystem> _logger;
    private readonly Kernel? _kernel;

    public MultiAgentCorrectionSystem(
        ILogger<MultiAgentCorrectionSystem> logger,
        Kernel? kernel = null)
    {
        _logger = logger;
        _kernel = kernel;
    }

    /// <summary>
    /// Orchestrate multi-agent correction process
    /// </summary>
    public async Task<MultiAgentCorrectionResult> CorrectSqlAsync(
        string failedSql,
        string error,
        string originalQuery,
        DatabaseSchema schema,
        int maxIterations = 3)
    {
        _logger.LogInformation(
            "[MultiAgentCorrection] Starting correction process for SQL error: {Error}",
            error);

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            _logger.LogDebug(
                "[MultiAgentCorrection] Iteration {Iteration}/{Max}",
                iteration + 1,
                maxIterations);

            try
            {
                // Step 1: Manager analyzes error and creates correction plan
                var plan = await CreateCorrectionPlanAsync(
                    failedSql,
                    error,
                    originalQuery,
                    schema);

                if (plan == null)
                {
                    _logger.LogWarning("[MultiAgentCorrection] Manager failed to create plan");
                    continue;
                }

                _logger.LogDebug(
                    "[MultiAgentCorrection] Plan created: {RootCause}",
                    plan.RootCause);

                // Step 2: Corrector applies corrections
                var correctedSql = await ApplyCorrectionAsync(
                    plan,
                    failedSql,
                    schema);

                if (string.IsNullOrEmpty(correctedSql))
                {
                    _logger.LogWarning("[MultiAgentCorrection] Corrector failed to generate SQL");
                    continue;
                }

                // Step 3: Reviewer validates correction
                var review = await ReviewCorrectionAsync(
                    failedSql,
                    correctedSql,
                    originalQuery,
                    plan);

                if (review.IsValid && review.Confidence > 0.8)
                {
                    _logger.LogInformation(
                        "[MultiAgentCorrection] Correction successful after {Iterations} iterations",
                        iteration + 1);

                    return MultiAgentCorrectionResult.Success(correctedSql, iteration + 1, plan, review);
                }

                // If not valid, use review feedback for next iteration
                _logger.LogDebug(
                    "[MultiAgentCorrection] Review failed: {Issues}",
                    string.Join("; ", review.Issues));

                error = string.Join(", ", review.Issues);
                failedSql = correctedSql;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[MultiAgentCorrection] Error in iteration {Iteration}",
                    iteration + 1);
            }
        }

        _logger.LogWarning(
            "[MultiAgentCorrection] Max iterations ({Max}) reached without success",
            maxIterations);

        return MultiAgentCorrectionResult.Failure($"Max iterations ({maxIterations}) reached");
    }

    /// <summary>
    /// Agent 1: Manager - Analyzes error and creates correction plan
    /// </summary>
    private async Task<CorrectionPlan?> CreateCorrectionPlanAsync(
        string failedSql,
        string error,
        string originalQuery,
        DatabaseSchema schema)
    {
        _logger.LogDebug("[Manager] Analyzing error and creating correction plan");

        if (_kernel == null)
        {
            _logger.LogWarning("[Manager] Kernel not available, cannot create correction plan");
            return null;
        }

        var schemaContext = FormatSchemaContext(schema);

        var prompt = $@"You are a SQL correction manager. Analyze this SQL error and create a correction plan.

User Question: {originalQuery}

Failed SQL:
{failedSql}

Error Message:
{error}

Database Schema:
{schemaContext}

Analyze the error and create a correction plan:
1. What is the root cause of the error?
2. What specific changes need to be made?
3. What alternative approaches could work?

Respond with JSON:
{{
  ""root_cause"": ""Brief description of the root cause"",
  ""changes"": [
    ""Change 1: description"",
    ""Change 2: description""
  ],
  ""alternatives"": [
    ""Alternative approach 1"",
    ""Alternative approach 2""
  ],
  ""confidence"": 0.0-1.0
}}

Be specific and actionable in your recommendations.";

        try
        {
            var response = await _kernel.InvokePromptAsync(prompt);
            var result = response.ToString();

            return ParseCorrectionPlan(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Manager] Failed to create correction plan");
            return null;
        }
    }

    /// <summary>
    /// Agent 2: Corrector - Applies corrections based on plan
    /// </summary>
    private async Task<string?> ApplyCorrectionAsync(
        CorrectionPlan plan,
        string failedSql,
        DatabaseSchema schema)
    {
        _logger.LogDebug("[Corrector] Applying corrections based on plan");

        if (_kernel == null)
        {
            _logger.LogWarning("[Corrector] Kernel not available, cannot apply corrections");
            return null;
        }

        var schemaContext = FormatSchemaContext(schema);
        var changesText = string.Join("\n", plan.Changes.Select((c, i) => $"{i + 1}. {c}"));

        var prompt = $@"You are a SQL corrector. Apply the correction plan to fix the SQL query.

Correction Plan:
Root Cause: {plan.RootCause}

Required Changes:
{changesText}

Failed SQL:
{failedSql}

Database Schema:
{schemaContext}

Apply the corrections and return ONLY the corrected SQL query.
Do not include explanations or markdown formatting.
The SQL must be syntactically correct and executable.";

        try
        {
            var response = await _kernel.InvokePromptAsync(prompt);
            var correctedSql = response.ToString().Trim();

            // Clean up markdown formatting if present
            correctedSql = CleanSqlResponse(correctedSql);

            return correctedSql;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Corrector] Failed to apply corrections");
            return null;
        }
    }

    /// <summary>
    /// Agent 3: Reviewer - Validates the correction
    /// </summary>
    private async Task<ReviewResult> ReviewCorrectionAsync(
        string originalSql,
        string correctedSql,
        string originalQuery,
        CorrectionPlan plan)
    {
        _logger.LogDebug("[Reviewer] Reviewing corrected SQL");

        if (_kernel == null)
        {
            _logger.LogWarning("[Reviewer] Kernel not available, cannot review correction");
            return new ReviewResult
            {
                IsValid = false,
                Confidence = 0.0,
                Issues = ["Kernel not available for review"],
                Improvements = []
            };
        }

        var prompt = $@"You are a SQL reviewer. Review this SQL correction for correctness.

User Question: {originalQuery}

Original (Failed) SQL:
{originalSql}

Corrected SQL:
{correctedSql}

Correction Plan Applied:
Root Cause: {plan.RootCause}
Changes: {string.Join(", ", plan.Changes)}

Review the corrected SQL and answer:
1. Does it fix the original error?
2. Does it still answer the user's question correctly?
3. Does it follow SQL best practices?
4. Are there any new issues introduced?

Respond with JSON:
{{
  ""is_valid"": true/false,
  ""confidence"": 0.0-1.0,
  ""issues"": [""issue1"", ""issue2""],
  ""improvements"": [""improvement1"", ""improvement2""]
}}

Be thorough and critical in your review.";

        try
        {
            var response = await _kernel.InvokePromptAsync(prompt);
            var result = response.ToString();

            return ParseReviewResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Reviewer] Failed to review correction");
            return new ReviewResult
            {
                IsValid = false,
                Confidence = 0.0,
                Issues = ["Review failed: " + ex.Message],
                Improvements = []
            };
        }
    }

    #region Helper Methods

    private string FormatSchemaContext(DatabaseSchema schema)
    {
        var tables = schema.Tables.Take(10).Select(t =>
        {
            var columns = string.Join(", ", t.Columns.Take(8).Select(c =>
            {
                var constraints = new List<string>();
                if (c.IsPrimaryKey) constraints.Add("PK");
                if (c.IsForeignKey) constraints.Add("FK");
                return $"{c.ColumnName} ({c.DataType}){(constraints.Any() ? " " + string.Join(" ", constraints) : "")}";
            }));

            return $"  {t.TableName}: {columns}";
        });

        return string.Join("\n", tables);
    }

    private CorrectionPlan? ParseCorrectionPlan(string response)
    {
        try
        {
            // Try JSON deserialization first
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<CorrectionPlan>(response, options);
        }
        catch
        {
            // Fallback to regex parsing
            _logger.LogDebug("[Manager] JSON parsing failed, using regex fallback");

            var rootCauseMatch = System.Text.RegularExpressions.Regex.Match(
                response,
                @"""root_cause"":\s*""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!rootCauseMatch.Success)
            {
                return null;
            }

            return new CorrectionPlan
            {
                RootCause = rootCauseMatch.Groups[1].Value,
                Changes = [],
                Alternatives = [],
                Confidence = 0.7
            };
        }
    }

    private ReviewResult ParseReviewResult(string response)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<ReviewResult>(response, options) ?? new ReviewResult
            {
                IsValid = false,
                Confidence = 0.0,
                Issues = ["Failed to parse review result"],
                Improvements = []
            };
        }
        catch
        {
            _logger.LogDebug("[Reviewer] JSON parsing failed, using regex fallback");

            var isValid = response.Contains("\"is_valid\": true", StringComparison.OrdinalIgnoreCase);

            return new ReviewResult
            {
                IsValid = isValid,
                Confidence = isValid ? 0.7 : 0.3,
                Issues = [],
                Improvements = []
            };
        }
    }

    private string CleanSqlResponse(string sql)
    {
        // Remove markdown code blocks
        sql = System.Text.RegularExpressions.Regex.Replace(
            sql,
            @"```(?:sql)?\s*\n?(.*?)\n?```",
            "$1",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return sql.Trim();
    }

    #endregion
}

/// <summary>
/// Correction plan created by Manager agent
/// </summary>
public class CorrectionPlan
{
    public string RootCause { get; set; } = "";
    public List<string> Changes { get; set; } = [];
    public List<string> Alternatives { get; set; } = [];
    public double Confidence { get; set; }
}

/// <summary>
/// Review result from Reviewer agent
/// </summary>
public class ReviewResult
{
    public bool IsValid { get; set; }
    public double Confidence { get; set; }
    public List<string> Issues { get; set; } = [];
    public List<string> Improvements { get; set; } = [];
}

/// <summary>
/// Final multi-agent correction result
/// </summary>
public class MultiAgentCorrectionResult
{
    public bool IsSuccessful { get; set; }
    public string? CorrectedSql { get; set; }
    public int Iterations { get; set; }
    public string? ErrorMessage { get; set; }
    public CorrectionPlan? Plan { get; set; }
    public ReviewResult? Review { get; set; }

    public static MultiAgentCorrectionResult Success(
        string correctedSql,
        int iterations,
        CorrectionPlan plan,
        ReviewResult review) => new()
        {
            IsSuccessful = true,
            CorrectedSql = correctedSql,
            Iterations = iterations,
            Plan = plan,
            Review = review
        };

    public static MultiAgentCorrectionResult Failure(string errorMessage) => new()
    {
        IsSuccessful = false,
        ErrorMessage = errorMessage
    };
}
