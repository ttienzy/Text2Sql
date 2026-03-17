using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// Enhanced ReAct Agent with conversation awareness
/// Maintains context across multiple turns in a conversation
/// </summary>
public class ConversationAwareReActAgent : IAgent
{
    private readonly IReasoningEngine _reasoningEngine;
    private readonly IReflectionEngine _reflectionEngine;
    private readonly ILLMToolSelector _toolSelector;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<ConversationAwareReActAgent> _logger;

    // Conversation state
    private readonly Dictionary<string, AgentState> _conversationStates = new();
    private readonly Dictionary<string, Dictionary<string, object>> _conversationMemories = new();

    public ConversationAwareReActAgent(
        IReasoningEngine reasoningEngine,
        IReflectionEngine reflectionEngine,
        ILLMToolSelector toolSelector,
        IToolRegistry toolRegistry,
        ILogger<ConversationAwareReActAgent> logger)
    {
        _reasoningEngine = reasoningEngine;
        _reflectionEngine = reflectionEngine;
        _toolSelector = toolSelector;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        // Convert to conversation-aware request if needed
        var conversationRequest = request as ConversationAwareAgentRequest ??
            new ConversationAwareAgentRequest(request.Question, request.DatabaseId);

        _logger.LogInformation("[ConversationAwareReActAgent] Starting execution for question: {Question} (Conversation: {ConversationId})",
            conversationRequest.Question, conversationRequest.ConversationId ?? "new");

        var context = CreateOrRestoreContext(conversationRequest);

        try
        {
            while (!context.IsComplete && context.Steps < conversationRequest.MaxSteps)
            {
                var stepNumber = context.Steps + 1;
                _logger.LogInformation("[ConversationAwareReActAgent] === Step {Step} (Conversation: {ConversationId}) ===",
                    stepNumber, conversationRequest.ConversationId);

                var step = new AgentStep { StepNumber = stepNumber };
                var stepwatch = Stopwatch.StartNew();

                try
                {
                    // ============================================
                    // THINK PHASE: Enhanced reasoning with conversation context
                    // ============================================
                    context.State.Status = "Thinking";
                    var (thought, plan) = await _reasoningEngine.ThinkAsync(context, ct);
                    step.Thought = thought;
                    step.Plan = plan;

                    // ============================================
                    // ACT PHASE: Select and execute tool with conversation awareness
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
                        _logger.LogWarning("[ConversationAwareReActAgent] No action selected, terminating");
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

                    // Store results in working memory with conversation context
                    if (observation.Success && observation.Result != null)
                    {
                        StoreResultInMemory(context, normalizedToolName, observation.Result);

                        // Store in conversation memory for future reference
                        if (!string.IsNullOrEmpty(conversationRequest.ConversationId))
                        {
                            StoreInConversationMemory(conversationRequest.ConversationId, normalizedToolName, observation.Result);
                        }
                    }

                    // ============================================
                    // REFLECT PHASE: Evaluate progress with conversation awareness
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
                        _logger.LogInformation("[ConversationAwareReActAgent] Terminating: {Reason}", reflection.TerminationReason);
                        context.State.Status = "Complete";
                        context.State.EndTime = DateTime.UtcNow;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ConversationAwareReActAgent] Error in step {Step}", stepNumber);
                    step.Observation = AgentObservation.FromError(ex.Message);
                    context.AddStep(step);

                    // Decide whether to continue or fail
                    if (context.Steps >= conversationRequest.MaxSteps - 1)
                    {
                        context.State.Status = "Failed";
                        context.WorkingMemory["error"] = $"Max steps reached with error: {ex.Message}";
                        break;
                    }
                }
            }

            // Max steps reached
            if (context.Steps >= conversationRequest.MaxSteps && !context.IsComplete)
            {
                _logger.LogWarning("[ConversationAwareReActAgent] Max steps ({Max}) reached", conversationRequest.MaxSteps);
                context.State.Status = "Failed";
                context.WorkingMemory["error"] = $"Max steps ({conversationRequest.MaxSteps}) reached";
            }

            context.State.EndTime = DateTime.UtcNow;
            var result = context.ToResult();

            // Store conversation state for future turns
            if (!string.IsNullOrEmpty(conversationRequest.ConversationId))
            {
                StoreConversationState(conversationRequest.ConversationId, context.State);
            }

            _logger.LogInformation("[ConversationAwareReActAgent] Execution complete. Success: {Success}, Steps: {Steps}",
                result.Success, result.TotalSteps);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConversationAwareReActAgent] Fatal error during execution");
            context.State.Status = "Failed";
            context.State.EndTime = DateTime.UtcNow;
            context.WorkingMemory["error"] = ex.Message;
            var result = context.ToResult();
            return result;
        }
    }

    /// <summary>
    /// Create new context or restore from conversation state
    /// </summary>
    private AgentContext CreateOrRestoreContext(ConversationAwareAgentRequest request)
    {
        var context = new AgentContext(request);

        // Add conversation history to context
        if (request.ConversationHistory.Any())
        {
            context.WorkingMemory["conversation_history"] = request.GetConversationContextForLLM();
            context.WorkingMemory["is_follow_up"] = request.IsRelatedToPreviousContext();

            // Add previous results to working memory for reference
            var lastAssistantMessage = request.ConversationHistory.LastOrDefault(m => m.Role == "assistant");
            if (lastAssistantMessage != null)
            {
                context.WorkingMemory["previous_query"] = lastAssistantMessage.SqlQuery;
                context.WorkingMemory["previous_result"] = lastAssistantMessage.Result;
            }
        }

        // Restore conversation memory if available
        if (!string.IsNullOrEmpty(request.ConversationId) &&
            _conversationMemories.TryGetValue(request.ConversationId, out var conversationMemory))
        {
            foreach (var kvp in conversationMemory)
            {
                context.WorkingMemory[$"conversation_{kvp.Key}"] = kvp.Value;
            }
        }

        context.State = new AgentState
        {
            Status = "Running",
            StartTime = DateTime.UtcNow
        };
        context.State.WorkingMemory = context.WorkingMemory;

        return context;
    }

    /// <summary>
    /// Store conversation state for future turns
    /// </summary>
    private void StoreConversationState(string conversationId, AgentState state)
    {
        _conversationStates[conversationId] = CloneState(state);
    }

    /// <summary>
    /// Store result in conversation memory for future reference
    /// </summary>
    private void StoreInConversationMemory(string conversationId, string toolName, object result)
    {
        if (!_conversationMemories.TryGetValue(conversationId, out var memory))
        {
            memory = new Dictionary<string, object>();
            _conversationMemories[conversationId] = memory;
        }

        memory[toolName] = result;

        // Keep only last 10 results to prevent memory bloat
        if (memory.Count > 10)
        {
            var oldestKey = memory.Keys.First();
            memory.Remove(oldestKey);
        }
    }

    /// <summary>
    /// Execute action with retry logic
    /// </summary>
    private async Task<AgentObservation> ExecuteActionWithRetryAsync(AgentAction action, int maxRetries, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await ExecuteActionAsync(action, ct);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "[ConversationAwareReActAgent] Action execution failed (attempt {Attempt}/{Max}): {Tool}",
                    attempt, maxRetries, action.ToolName);

                // Wait before retry
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        // Final attempt without catch
        return await ExecuteActionAsync(action, ct);
    }

    /// <summary>
    /// Execute a single action
    /// </summary>
    private async Task<AgentObservation> ExecuteActionAsync(AgentAction action, CancellationToken ct)
    {
        var tool = _toolRegistry.GetTool(action.ToolName);
        if (tool == null)
        {
            return AgentObservation.FromError($"Tool '{action.ToolName}' not found");
        }

        _logger.LogInformation("[ConversationAwareReActAgent] Executing tool: {Tool} with parameters: {Parameters}",
            action.ToolName, System.Text.Json.JsonSerializer.Serialize(action.Parameters));

        var toolInput = new ToolInput { Parameters = action.Parameters };
        var result = await tool.ExecuteAsync(toolInput, ct);

        return new AgentObservation
        {
            Success = result.Success,
            Result = result.Data,
            ErrorMessage = result.ErrorMessage
        };
    }

    /// <summary>
    /// Store result in working memory
    /// </summary>
    private void StoreResultInMemory(AgentContext context, string toolName, object result)
    {
        context.WorkingMemory[toolName] = result;

        // Store with timestamp for conversation context
        context.WorkingMemory[$"{toolName}_timestamp"] = DateTime.UtcNow;
    }

    /// <summary>
    /// Estimate token usage for cost tracking
    /// </summary>
    private int EstimateTokens(string thought, string plan, AgentAction action, AgentObservation observation)
    {
        var text = $"{thought} {plan} {System.Text.Json.JsonSerializer.Serialize(action.Parameters)} {observation.Result}";
        return text.Length / 4; // Rough estimation: 4 chars per token
    }

    /// <summary>
    /// Normalize tool name for consistent storage
    /// </summary>
    private static string NormalizeToolName(string toolName)
    {
        return toolName.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
    }

    /// <summary>
    /// Clone agent state for storage
    /// </summary>
    private static AgentState CloneState(AgentState source)
    {
        return new AgentState
        {
            Status = source.Status,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            CurrentStep = source.CurrentStep,
            History = new List<AgentStep>(source.History),
            WorkingMemory = new Dictionary<string, object>(source.WorkingMemory)
        };
    }

    /// <summary>
    /// Get current state for debugging
    /// </summary>
    public AgentState GetState()
    {
        return new AgentState
        {
            Status = "Ready",
            StartTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Reset conversation state
    /// </summary>
    public void Reset()
    {
        _conversationStates.Clear();
        _conversationMemories.Clear();
        _logger.LogInformation("[ConversationAwareReActAgent] Reset all conversation states");
    }

    /// <summary>
    /// Reset specific conversation state
    /// </summary>
    public void ResetConversation(string conversationId)
    {
        _conversationStates.Remove(conversationId);
        _conversationMemories.Remove(conversationId);
        _logger.LogInformation("[ConversationAwareReActAgent] Reset conversation state: {ConversationId}", conversationId);
    }

    /// <summary>
    /// Clear all conversation states (for memory management)
    /// </summary>
    public void ClearAllConversations()
    {
        _conversationStates.Clear();
        _conversationMemories.Clear();
        _logger.LogInformation("[ConversationAwareReActAgent] Cleared all conversation states");
    }
}