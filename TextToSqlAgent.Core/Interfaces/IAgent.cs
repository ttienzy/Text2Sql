using TextToSqlAgent.Core.Agent;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for AI agents that can process requests and return results
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Process an agent request and return the result
    /// </summary>
    /// <param name="request">The agent request to process</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent result</returns>
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get the current state of the agent
    /// </summary>
    /// <returns>The current agent state</returns>
    AgentState GetState();

    /// <summary>
    /// Reset the agent state
    /// </summary>
    void Reset();
}