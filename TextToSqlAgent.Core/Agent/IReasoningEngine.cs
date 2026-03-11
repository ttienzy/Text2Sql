namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Engine for agent reasoning (THINK phase)
/// </summary>
public interface IReasoningEngine
{
    /// <summary>
    /// Generate thought and plan for next action
    /// </summary>
    Task<(string Thought, string Plan)> ThinkAsync(
        AgentContext context,
        CancellationToken ct = default);
}
