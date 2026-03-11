namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Core agent interface - represents an autonomous agent that can reason and act
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Run the agent to complete a request
    /// </summary>
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get agent's current state
    /// </summary>
    AgentState GetState();

    /// <summary>
    /// Reset agent state
    /// </summary>
    void Reset();
}
