namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Engine for agent reflection (REFLECT phase)
/// </summary>
public interface IReflectionEngine
{
    /// <summary>
    /// Reflect on observation and decide next steps
    /// </summary>
    Task<AgentReflection> ReflectAsync(
        AgentContext context,
        AgentObservation observation,
        CancellationToken ct = default);
}
