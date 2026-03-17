using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Infrastructure.Observability;

/// <summary>
/// Decorator for IAgent that adds telemetry and observability
/// </summary>
public class ObservableAgent : IAgent
{
    private readonly IAgent _innerAgent;
    private readonly TelemetryService _telemetry;
    private readonly ILogger<ObservableAgent> _logger;

    public ObservableAgent(
        IAgent innerAgent,
        TelemetryService telemetry,
        ILogger<ObservableAgent> logger)
    {
        _innerAgent = innerAgent;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        using var activity = _telemetry.TrackAgentExecution(request.Question, request.DatabaseId);

        try
        {
            _logger.LogInformation(
                "Agent execution started | Question: {Question} | Database: {Database}",
                request.Question,
                request.DatabaseId ?? "unknown");

            var result = await _innerAgent.RunAsync(request, ct);

            // Record metrics
            _telemetry.RecordMetric("agent.steps", result.TotalSteps);
            _telemetry.RecordMetric("agent.tokens", result.TotalTokensUsed);
            _telemetry.RecordMetric("agent.latency_ms", result.TotalLatencyMs);

            if (result.Success)
            {
                _telemetry.RecordMetric("agent.success", 1);
                _logger.LogInformation(
                    "Agent execution succeeded | Steps: {Steps} | Latency: {Latency}ms | Tokens: {Tokens}",
                    result.TotalSteps,
                    result.TotalLatencyMs,
                    result.TotalTokensUsed);
            }
            else
            {
                _telemetry.RecordMetric("agent.failure", 1);
                _logger.LogWarning(
                    "Agent execution failed | Steps: {Steps} | Error: {Error}",
                    result.TotalSteps,
                    result.ErrorMessage ?? "Unknown error");
            }

            return result;
        }
        catch (Exception ex)
        {
            _telemetry.LogError(ex, "AgentExecution", new Dictionary<string, object>
            {
                ["question"] = request.Question,
                ["database"] = request.DatabaseId ?? "unknown"
            });

            throw;
        }
    }

    public AgentState GetState()
    {
        return _innerAgent.GetState();
    }

    public void Reset()
    {
        _innerAgent.Reset();
        _logger.LogInformation("Agent state reset");
    }
}
