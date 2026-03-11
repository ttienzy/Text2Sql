using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// ReAct (Reasoning + Acting) Agent implementation
/// Follows the pattern: Think → Act → Observe → Reflect → Loop
/// Enhanced with LLM-based tool selection
/// </summary>
public class ReActAgent : IAgent
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IReasoningEngine _reasoningEngine;
    private readonly IReflectionEngine _reflectionEngine;
    private readonly LLMToolSelector _toolSelector;
    private readonly ILogger<ReActAgent> _logger;

    private readonly object _stateLock = new();
    private AgentState _lastState = new();

    public ReActAgent(
        IToolRegistry toolRegistry,
        IReasoningEngine reasoningEngine,
        IReflectionEngine reflectionEngine,
        LLMToolSelector toolSelector,
        ILogger<ReActAgent> logger)
    {
        _toolRegistry = toolRegistry;
        _reasoningEngine = reasoningEngine;
        _reflectionEngine = reflectionEngine;
        _toolSelector = toolSelector;
        _logger = logger;
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("[ReActAgent] Starting execution for question: {Question}", request.Question);

        var context = new AgentContext(request);
        context.State = new AgentState
        {
            Status = "Running",
            StartTime = DateTime.UtcNow
        };
        context.State.WorkingMemory = context.WorkingMemory;

        try
        {
            while (!context.IsComplete && context.Steps < request.MaxSteps)
            {
                var stepNumber = context.Steps + 1;
                _logger.LogInformation("[ReActAgent] === Step {Step} ===", stepNumber);

                var step = new AgentStep { StepNumber = stepNumber };
                var stepwatch = Stopwatch.StartNew();

                try
                {
                    // ============================================
                    // THINK PHASE: Agent reasons about what to do
                    // ============================================
                    context.State.Status = "Thinking";
                    var (thought, plan) = await _reasoningEngine.ThinkAsync(context, ct);
                    step.Thought = thought;
                    step.Plan = plan;

                    // ============================================
                    // ACT PHASE: Select and execute tool
                    // ============================================
                    context.State.Status = "Acting";
                    var availableTools = _toolRegistry.GetAllTools();
                    var action = await _toolSelector.SelectActionAsync(
                        thought,
                        plan,
                        context,
                        availableTools,
                        ct);

                    step.Action = action;

                    if (action == null)
                    {
                        _logger.LogWarning("[ReActAgent] No action selected, terminating");
                        context.State.Status = "Failed";
                        context.WorkingMemory["error"] = "Failed to select action";
                        break;
                    }

                    // ============================================
                    // OBSERVE PHASE: Execute tool and capture result
                    // ============================================
                    context.State.Status = "Observing";
                    var normalizedToolName = NormalizeToolName(action.ToolName);
                    var observation = await ExecuteActionWithRetryAsync(action, 3, ct);
                    step.Observation = observation;

                    // Store results in working memory
                    if (observation.Success && observation.Result != null)
                    {
                        StoreResultInMemory(context, normalizedToolName, observation.Result);
                    }

                    // ============================================
                    // REFLECT PHASE: Evaluate progress
                    // ============================================
                    context.State.Status = "Reflecting";
                    var reflection = await _reflectionEngine.ReflectAsync(context, observation, ct);
                    step.Reflection = reflection;

                    // Update metrics
                    stepwatch.Stop();
                    step.LatencyMs = stepwatch.ElapsedMilliseconds;
                    step.TokensUsed = EstimateTokens(thought, plan, action, observation);

                    // Add step to history
                    context.AddStep(step);

                    // Check termination
                    if (reflection.ShouldTerminate)
                    {
                        _logger.LogInformation("[ReActAgent] Terminating: {Reason}", reflection.TerminationReason);
                        context.State.Status = "Complete";
                        context.State.EndTime = DateTime.UtcNow;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ReActAgent] Error in step {Step}", stepNumber);
                    step.Observation = AgentObservation.FromError(ex.Message);
                    context.AddStep(step);

                    // Decide whether to continue or fail
                    if (context.Steps >= request.MaxSteps - 1)
                    {
                        context.State.Status = "Failed";
                        context.WorkingMemory["error"] = $"Max steps reached with error: {ex.Message}";
                        break;
                    }
                }
            }

            // Max steps reached
            if (context.Steps >= request.MaxSteps && !context.IsComplete)
            {
                _logger.LogWarning("[ReActAgent] Max steps ({Max}) reached", request.MaxSteps);
                context.State.Status = "Failed";
                context.WorkingMemory["error"] = $"Max steps ({request.MaxSteps}) reached";
            }

            context.State.EndTime = DateTime.UtcNow;
            var result = context.ToResult();

            _logger.LogInformation("[ReActAgent] Execution complete. Success: {Success}, Steps: {Steps}",
                result.Success, result.TotalSteps);

            UpdateLastStateSnapshot(context.State);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ReActAgent] Fatal error during execution");
            context.State.Status = "Failed";
            context.State.EndTime = DateTime.UtcNow;
            context.WorkingMemory["error"] = ex.Message;
            var result = context.ToResult();
            UpdateLastStateSnapshot(context.State);
            return result;
        }
    }

    private async Task<AgentObservation> ExecuteActionWithRetryAsync(
        AgentAction action,
        int maxRetries,
        CancellationToken ct)
    {
        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount < maxRetries)
        {
            try
            {
                return await ExecuteActionAsync(action, ct);
            }
            catch (Exception ex)
            {
                lastException = ex;
                retryCount++;

                if (retryCount < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff
                    _logger.LogWarning(ex,
                        "[ReActAgent] Tool execution failed, retry {Retry}/{Max} after {Delay}s",
                        retryCount, maxRetries, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
        }

        return AgentObservation.FromError(
            $"Tool execution failed after {maxRetries} retries: {lastException?.Message}");
    }

    private async Task<AgentObservation> ExecuteActionAsync(AgentAction action, CancellationToken ct)
    {
        try
        {
            // Special case: finish action
            var normalizedToolName = NormalizeToolName(action.ToolName);

            if (normalizedToolName == "finish")
            {
                return AgentObservation.FromSuccess("Task complete");
            }

            var tool = _toolRegistry.GetTool(normalizedToolName);
            if (tool == null)
            {
                return AgentObservation.FromError($"Tool '{normalizedToolName}' not found");
            }

            var input = new ToolInput { Parameters = action.Parameters };
            var result = await tool.ExecuteAsync(input, ct);

            if (result.Success)
            {
                return AgentObservation.FromSuccess(result.Data);
            }
            else
            {
                return AgentObservation.FromError(result.ErrorMessage ?? "Tool execution failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ReActAgent] Error executing action: {Tool}", action.ToolName);
            return AgentObservation.FromError(ex.Message);
        }
    }

    private void StoreResultInMemory(AgentContext context, string toolName, object result)
    {
        // Store tool results in working memory for future steps
        switch (toolName)
        {
            case "explore_schema":
                context.WorkingMemory["schema_context"] = result;
                _logger.LogDebug("[ReActAgent] Stored schema context in memory");
                break;

            case "generate_sql":
                context.WorkingMemory["sql"] = result;
                _logger.LogDebug("[ReActAgent] Stored SQL in memory");
                break;

            case "execute_sql":
                context.WorkingMemory["query_result"] = result;
                context.WorkingMemory["query_results"] = result; // Backward compatibility for older consumers
                _logger.LogDebug("[ReActAgent] Stored query results in memory");
                break;

            case "validate_sql":
                context.WorkingMemory["validation_result"] = result;
                _logger.LogDebug("[ReActAgent] Stored validation result in memory");
                break;

            case "decompose_query":
                context.WorkingMemory["sub_queries"] = result;
                _logger.LogDebug("[ReActAgent] Stored sub-queries in memory");
                break;

            default:
                context.WorkingMemory[$"{toolName}_result"] = result;
                _logger.LogDebug("[ReActAgent] Stored {Tool} result in memory", toolName);
                break;
        }
    }

    private int EstimateTokens(string thought, string plan, AgentAction action, AgentObservation observation)
    {
        // Rough estimate: 1 token ≈ 4 characters
        var text = thought + plan + JsonSerializer.Serialize(action) + JsonSerializer.Serialize(observation);
        return text.Length / 4;
    }

    public AgentState GetState()
    {
        lock (_stateLock)
        {
            return CloneState(_lastState);
        }
    }

    public void Reset()
    {
        lock (_stateLock)
        {
            _lastState = new AgentState();
        }

        _logger.LogInformation("[ReActAgent] State reset");
    }

    private static string NormalizeToolName(string toolName)
    {
        return toolName switch
        {
            "verify_results" => "verify_result",
            _ => toolName
        };
    }

    private void UpdateLastStateSnapshot(AgentState state)
    {
        lock (_stateLock)
        {
            _lastState = CloneState(state);
        }
    }

    private static AgentState CloneState(AgentState source)
    {
        return new AgentState
        {
            Status = source.Status,
            CurrentStep = source.CurrentStep,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            WorkingMemory = source.WorkingMemory.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            History = source.History.Select(step => new AgentStep
            {
                StepNumber = step.StepNumber,
                Timestamp = step.Timestamp,
                Thought = step.Thought,
                Plan = step.Plan,
                LatencyMs = step.LatencyMs,
                TokensUsed = step.TokensUsed,
                Action = step.Action == null
                    ? null
                    : new AgentAction
                    {
                        ToolName = step.Action.ToolName,
                        Reasoning = step.Action.Reasoning,
                        Parameters = step.Action.Parameters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    },
                Observation = step.Observation == null
                    ? null
                    : new AgentObservation
                    {
                        Success = step.Observation.Success,
                        Result = step.Observation.Result,
                        ErrorMessage = step.Observation.ErrorMessage,
                        Metadata = step.Observation.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    },
                Reflection = step.Reflection == null
                    ? null
                    : new AgentReflection
                    {
                        Assessment = step.Reflection.Assessment,
                        ShouldTerminate = step.Reflection.ShouldTerminate,
                        TerminationReason = step.Reflection.TerminationReason,
                        NextAction = step.Reflection.NextAction,
                        Confidence = step.Reflection.Confidence
                    }
            }).ToList()
        };
    }
}
