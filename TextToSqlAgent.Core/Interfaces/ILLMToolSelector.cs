using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for LLM-based tool selection
/// </summary>
public interface ILLMToolSelector
{
    /// <summary>
    /// Select an action based on the thought, plan, and available tools
    /// </summary>
    /// <param name="thought">The agent's current thought</param>
    /// <param name="plan">The agent's current plan</param>
    /// <param name="context">The current agent context</param>
    /// <param name="availableTools">List of available tools</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The selected action, or null if no action should be taken</returns>
    Task<AgentAction?> SelectActionAsync(
        string thought,
        string plan,
        AgentContext context,
        IEnumerable<ITool> availableTools,
        CancellationToken ct = default);
}