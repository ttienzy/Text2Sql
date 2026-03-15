using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Infrastructure.Analysis;
using TextToSqlAgent.Infrastructure.Services;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Orchestrates Human-in-the-Loop workflow including ambiguity detection and DML confirmation
/// </summary>
public interface IHumanInTheLoopOrchestrator
{
    /// <summary>
    /// Check if generated SQL requires user confirmation
    /// Returns sessionId if clarification is needed, null otherwise
    /// </summary>
    Task<string?> CheckAndRequestConfirmationAsync(
        string userId,
        string question,
        string? sql,
        IProgress<AgentStepEvent>? progress,
        CancellationToken ct = default);
}

public class HumanInTheLoopOrchestrator : IHumanInTheLoopOrchestrator
{
    private readonly IAgentSessionService _sessionService;
    private readonly DmlDetector _dmlDetector;
    private readonly ILogger<HumanInTheLoopOrchestrator> _logger;

    public HumanInTheLoopOrchestrator(
        IAgentSessionService sessionService,
        DmlDetector dmlDetector,
        ILogger<HumanInTheLoopOrchestrator> logger)
    {
        _sessionService = sessionService;
        _dmlDetector = dmlDetector;
        _logger = logger;
    }

    public async Task<string?> CheckAndRequestConfirmationAsync(
        string userId,
        string question,
        string? sql,
        IProgress<AgentStepEvent>? progress,
        CancellationToken ct = default)
    {
        // If no SQL generated, no confirmation needed
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        // Check if SQL is DML
        var dmlAnalysis = _dmlDetector.Analyze(sql);

        if (dmlAnalysis.RequiresConfirmation)
        {
            _logger.LogInformation(
                "DML query detected: {DmlTypes}. Requesting user confirmation",
                string.Join(", ", dmlAnalysis.DmlTypes));

            // Save session state for later resumption
            var sessionId = await _sessionService.SaveSessionAsync(
                userId,
                question,
                null, // Agent state - would need to be passed from agent
                null, // Working memory
                null, // Steps
                ClarificationType.dml_confirmation,
                sql,
                null, // conversationId
                ct);

            // Emit clarification event
            progress?.Report(AgentStepEvent.NeedsClarification(
                sessionId,
                ClarificationType.dml_confirmation,
                dmlAnalysis.SqlPreview ?? "This query will modify data",
                new List<string> { "Yes, execute", "No, cancel" },
                300));

            return sessionId;
        }

        // Check if it's a SELECT - safe to execute
        if (_dmlDetector.IsSelectOnly(sql))
        {
            _logger.LogDebug("SQL is SELECT - no confirmation needed");
            return null;
        }

        _logger.LogDebug("SQL does not require confirmation");
        return null;
    }
}