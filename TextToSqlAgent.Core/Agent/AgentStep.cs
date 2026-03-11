namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Represents one step in the agent's reasoning loop (ReAct pattern)
/// </summary>
public class AgentStep
{
    public int StepNumber { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // THINK phase
    public string Thought { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;

    // ACT phase
    public AgentAction? Action { get; set; }

    // OBSERVE phase
    public AgentObservation? Observation { get; set; }

    // REFLECT phase
    public AgentReflection? Reflection { get; set; }

    // Metrics
    public long LatencyMs { get; set; }
    public int TokensUsed { get; set; }
}
