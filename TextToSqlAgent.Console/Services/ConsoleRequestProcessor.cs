using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Console.Services;

/// <summary>
/// Processes console requests with intelligent routing
/// Routes fast-path queries (greeting, out-of-scope) without loading schema
/// Routes database queries to full agent pipeline
/// </summary>
public class ConsoleRequestProcessor
{
    private readonly IQueryRouter _router;
    private readonly EnhancedAgentOrchestrator _agent;
    private readonly ILogger<ConsoleRequestProcessor> _logger;

    public ConsoleRequestProcessor(
        IQueryRouter router,
        EnhancedAgentOrchestrator agent,
        ILogger<ConsoleRequestProcessor> logger)
    {
        _router = router;
        _agent = agent;
        _logger = logger;
    }

    public async Task<AgentResponse> ProcessAsync(
        string question,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing request: {Question}", question.Substring(0, Math.Min(50, question.Length)));
        var startTime = DateTime.UtcNow;

        // Step 1: Route query
        var route = await _router.RouteAsync(question, conversationId, cancellationToken);

        var routeTime = DateTime.UtcNow - startTime;
        _logger.LogDebug(
            "Route: {Type}, Confidence: {Confidence:P0}, RequiresLLM: {LLM}, Time: {Ms}ms",
            route.Type, route.Confidence, route.RequiresLLM, routeTime.TotalMilliseconds);

        // Step 2: Fast path for greeting/out-of-scope
        if (!route.RequiresLLM && route.DirectResponse != null)
        {
            _logger.LogInformation("Fast path response (no LLM needed)");

            return new AgentResponse
            {
                Success = true,
                Answer = route.DirectResponse,
                ProcessingSteps = new List<string>
                {
                    $"Fast path: {route.Type}",
                    $"Reason: {route.Reason}",
                    $"Route time: {routeTime.TotalMilliseconds:F0}ms"
                }
            };
        }

        // Step 3: Full pipeline for database queries
        _logger.LogInformation("Using agentic AI pipeline for database query");

        return await _agent.ProcessQueryAsync(
            question,
            conversationId,
            conversationHistory: null,
            progress: null,
            sqlTokenCallback: null,
            cancellationToken);
    }
}
