using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services.Advanced;

/// <summary>
/// Confidence-based escalation using multiple signals
/// Inspired by Error Detection papers (2024)
/// </summary>
public class ConfidenceEstimator
{
    private readonly ILogger<ConfidenceEstimator> _logger;
    private readonly Kernel? _kernel;

    public ConfidenceEstimator(
        ILogger<ConfidenceEstimator> logger,
        Kernel? kernel = null)
    {
        _logger = logger;
        _kernel = kernel;
    }

    /// <summary>
    /// Estimate confidence using multiple signals
    /// </summary>
    public async Task<ConfidenceScore> EstimateAsync(
        string query,
        string generatedSql,
        IntentClassificationResult intent,
        DatabaseSchema schema)
    {
        _logger.LogDebug("[ConfidenceEstimator] Estimating confidence for SQL generation");

        var signals = new List<double>();

        // Signal 1: Intent classification confidence
        signals.Add(intent.Confidence);

        // Signal 2: SQL complexity vs query complexity alignment
        var sqlComplexity = AnalyzeSqlComplexity(generatedSql);
        // Use intent type as proxy for complexity
        var intentComplexity = intent.Intent.ToString();
        var alignmentScore = CalculateAlignment(intentComplexity, sqlComplexity);
        signals.Add(alignmentScore);

        // Signal 3: Schema coverage
        var schemaCoverage = CalculateSchemaCoverage(query, schema);
        signals.Add(schemaCoverage);

        // Signal 4: LLM self-assessment (optional, can be expensive)
        // var selfAssessment = await GetLlmSelfAssessmentAsync(query, generatedSql);
        // signals.Add(selfAssessment);

        // Weighted average
        var confidence = signals.Average();

        var score = new ConfidenceScore
        {
            Overall = confidence,
            Signals = signals,
            ShouldEscalate = confidence < 0.7,
            Reasoning = BuildReasoningFromSignals(signals)
        };

        _logger.LogInformation(
            "[ConfidenceEstimator] Confidence: {Confidence:F2}, Should escalate: {ShouldEscalate}",
            confidence,
            score.ShouldEscalate);

        return score;
    }

    #region Helper Methods

    private double AnalyzeSqlComplexity(string sql)
    {
        // Simple heuristic: count keywords
        var keywords = new[] { "JOIN", "SUBQUERY", "CTE", "WITH", "UNION", "GROUP BY", "HAVING" };
        var count = keywords.Count(k => sql.Contains(k, StringComparison.OrdinalIgnoreCase));

        return Math.Min(count / 5.0, 1.0); // Normalize to 0-1
    }

    private double CalculateAlignment(string intentComplexity, double sqlComplexity)
    {
        // Simple alignment check
        var expectedComplexity = intentComplexity.ToLowerInvariant() switch
        {
            "simple" => 0.2,
            "medium" => 0.5,
            "complex" => 0.8,
            _ => 0.5
        };

        var diff = Math.Abs(expectedComplexity - sqlComplexity);
        return 1.0 - diff; // Higher score = better alignment
    }

    private double CalculateSchemaCoverage(string query, DatabaseSchema schema)
    {
        // Check if mentioned entities exist in schema
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchedTables = 0;

        foreach (var word in words)
        {
            if (schema.Tables.Any(t => t.TableName.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                matchedTables++;
            }
        }

        return matchedTables > 0 ? 0.8 : 0.5; // Simple binary check
    }

    private string BuildReasoningFromSignals(List<double> signals)
    {
        var reasons = new List<string>();

        if (signals[0] < 0.7)
            reasons.Add("Low intent confidence");

        if (signals.Count > 1 && signals[1] < 0.7)
            reasons.Add("SQL complexity mismatch");

        if (signals.Count > 2 && signals[2] < 0.7)
            reasons.Add("Low schema coverage");

        return reasons.Any()
            ? string.Join(", ", reasons)
            : "All signals positive";
    }

    #endregion
}

/// <summary>
/// Confidence score with multiple signals
/// </summary>
public class ConfidenceScore
{
    public double Overall { get; set; }
    public List<double> Signals { get; set; } = [];
    public bool ShouldEscalate { get; set; }
    public string Reasoning { get; set; } = "";
}
