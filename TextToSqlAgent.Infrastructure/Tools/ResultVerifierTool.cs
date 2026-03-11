using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Verification;

namespace TextToSqlAgent.Infrastructure.Tools;

/// <summary>
/// Tool for verifying SQL execution results
/// </summary>
public class ResultVerifierTool : ITool
{
    private readonly ResultVerifier _verifier;
    private readonly ILogger<ResultVerifierTool> _logger;

    public string Name => "verify_result";
    public string Description => "Verify if SQL execution result makes sense for the given question";

    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new() {
                Name = "question",
                Type = "string",
                Description = "The original natural language question",
                Required = true
            },
            new() {
                Name = "sql",
                Type = "string",
                Description = "The SQL query that was executed",
                Required = true
            },
            new() {
                Name = "execution_result",
                Type = "object",
                Description = "The SQL execution result to verify",
                Required = true
            }
        }
    };

    public ResultVerifierTool(
        ResultVerifier verifier,
        ILogger<ResultVerifierTool> logger)
    {
        _verifier = verifier;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var question = input.GetString("question", "query");
            var sql = input.GetString("sql");
            var executionResult = input.Get<SqlExecutionResult>(
                "execution_result",
                "query_result",
                "query_results",
                "results");

            if (executionResult == null)
            {
                return ToolResult.FromError("execution_result is required");
            }

            _logger.LogInformation("Verifying result for question: {Question}", question);

            var verification = await _verifier.VerifyAsync(question, sql, executionResult, ct);

            return ToolResult.FromSuccess(new
            {
                is_valid = verification.IsValid,
                confidence = verification.Confidence,
                issues = verification.Issues,
                suggestion = verification.Suggestion,
                summary = verification.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Result verification failed");
            return ToolResult.FromError($"Verification failed: {ex.Message}");
        }
    }
}
