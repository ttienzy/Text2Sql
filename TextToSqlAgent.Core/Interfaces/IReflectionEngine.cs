using TextToSqlAgent.Core.Agent;

namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// Interface for reflection engines that evaluate agent progress
/// </summary>
public interface IReflectionEngine
{
    /// <summary>
    /// Reflect on the current context and observation to determine next steps
    /// </summary>
    /// <param name="context">The current agent context</param>
    /// <param name="observation">The observation from the last action</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A reflection result indicating whether to continue or terminate</returns>
    Task<AgentReflection> ReflectAsync(AgentContext context, AgentObservation observation, CancellationToken ct = default);
}