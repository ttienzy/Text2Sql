using TextToSqlAgent.Core.Agent;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for reasoning engines that generate thoughts and plans
/// </summary>
public interface IReasoningEngine
{
    /// <summary>
    /// Generate a thought and plan for the current context
    /// </summary>
    /// <param name="context">The current agent context</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A tuple containing the thought and plan</returns>
    Task<(string Thought, string Plan)> ThinkAsync(AgentContext context, CancellationToken ct = default);
}