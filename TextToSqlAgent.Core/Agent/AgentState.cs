namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Current state of the agent
/// </summary>
public class AgentState
{
    public string Status { get; set; } = "Idle"; // Idle, Thinking, Acting, Observing, Reflecting, Complete, Failed
    public int CurrentStep { get; set; }
    public List<AgentStep> History { get; set; } = new();
    public Dictionary<string, object> WorkingMemory { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}
