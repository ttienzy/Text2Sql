using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Evaluation.Metrics;
using TextToSqlAgent.Evaluation.Models;
using TextToSqlAgent.Evaluation.Validators;

namespace TextToSqlAgent.Evaluation.Runners;

/// <summary>
/// Evaluates the ReAct Agent system
/// P1-08: Enhanced with proper result validation
/// </summary>
public class ReActAgentEvaluator
{
    private readonly IAgent _agent;
    private readonly MetricsCalculator _metricsCalculator;
    private readonly ResultValidator _resultValidator;
    private readonly ILogger<ReActAgentEvaluator> _logger;

    public ReActAgentEvaluator(
        IAgent agent,
        MetricsCalculator metricsCalculator,
        ResultValidator resultValidator,
        ILogger<ReActAgentEvaluator> logger)
    {
        _agent = agent;
        _metricsCalculator = metricsCalculator;
        _resultValidator = resultValidator;
        _logger = logger;
    }

    public async Task<EvaluationReport> RunEvaluationAsync(
        List<EvaluationExample> examples,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting ReAct Agent evaluation with {Count} examples", examples.Count);

        var results = new List<EvaluationResult>();
        int completed = 0;

        foreach (var example in examples)
        {
            try
            {
                // Reset agent state before each example
                _agent.Reset();

                var stopwatch = Stopwatch.StartNew();

                var request = new AgentRequest(example.Question, example.DatabaseId)
                {
                    MaxSteps = 10
                };

                var agentResult = await _agent.RunAsync(request, ct);

                stopwatch.Stop();

                // Convert AgentResult to AgentResponse for compatibility
                var response = new Core.Models.AgentResponse
                {
                    Success = agentResult.Success,
                    Answer = agentResult.Answer,
                    SqlGenerated = agentResult.SqlGenerated,
                    QueryResult = agentResult.QueryResult as Core.Models.SqlExecutionResult,
                    ProcessingSteps = agentResult.ProcessingSteps,
                    ErrorMessage = agentResult.ErrorMessage
                };

                var result = new EvaluationResult
                {
                    Example = example,
                    AgentResponse = response,
                    ExecutionSuccess = agentResult.Success,
                    LatencyMs = stopwatch.ElapsedMilliseconds,
                    TokensUsed = agentResult.TotalTokensUsed
                };

                // Calculate schema linking metrics
                CalculateSchemaLinkingMetrics(result);

                // Compare SQL
                result.ExactMatch = CompareSql(agentResult.SqlGenerated, example.GroundTruthSql);

                // P1-08: Validate actual results against expected results
                result.ResultMatch = _resultValidator.ValidateResults(
                    response.QueryResult,
                    example.ExpectedResults);

                // P1-08: Calculate result similarity for partial credit
                result.ResultSimilarity = _resultValidator.CalculateSimilarity(
                    response.QueryResult,
                    example.ExpectedResults);

                // P1-08: Add validation failure reason if results don't match
                if (agentResult.Success && !result.ResultMatch)
                {
                    result.ValidationFailureReason =
                        $"Query executed successfully but results don't match expected. " +
                        $"Similarity: {result.ResultSimilarity:F2}%";
                }

                results.Add(result);

                completed++;
                _logger.LogInformation(
                    "Completed {Completed}/{Total}: {Id} - ExecutionSuccess: {ExecSuccess}, ResultMatch: {ResultMatch}, Steps: {Steps}",
                    completed, examples.Count, example.Id, agentResult.Success, result.ResultMatch, agentResult.TotalSteps);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate example {Id}", example.Id);
                results.Add(EvaluationResult.Failed(example, ex));
            }
        }

        var report = _metricsCalculator.GenerateReport(results, "react_agent");

        _logger.LogInformation(
            "Evaluation complete. ExecutionAccuracy: {ExecAcc:F2}%, ResultAccuracy: {ResAcc:F2}%",
            report.ExecutionAccuracy, report.ResultAccuracy);

        return report;
    }

    private void CalculateSchemaLinkingMetrics(EvaluationResult result)
    {
        var example = result.Example;
        var sql = result.AgentResponse?.SqlGenerated ?? "";

        // Extract tables mentioned in generated SQL
        var mentionedTables = example.RequiredTables
            .Where(t => sql.Contains(t, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Precision: How many mentioned tables are correct?
        result.SchemaLinkingPrecision = example.RequiredTables.Count > 0
            ? (double)mentionedTables.Count / example.RequiredTables.Count * 100
            : 100.0;

        // Recall: How many required tables were found?
        result.SchemaLinkingRecall = example.RequiredTables.Count > 0
            ? (double)mentionedTables.Count / example.RequiredTables.Count * 100
            : 100.0;
    }

    private bool CompareSql(string? generated, string groundTruth)
    {
        if (string.IsNullOrEmpty(generated)) return false;

        // Normalize SQL for comparison
        var normalizedGenerated = NormalizeSql(generated);
        var normalizedGroundTruth = NormalizeSql(groundTruth);

        return normalizedGenerated.Equals(normalizedGroundTruth, StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeSql(string sql)
    {
        // Remove extra whitespace, newlines, and normalize case
        return string.Join(" ", sql.Split(new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToUpperInvariant();
    }
}
