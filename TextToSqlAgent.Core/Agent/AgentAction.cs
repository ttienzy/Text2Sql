namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Action taken by the agent
/// </summary>
public class AgentAction
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
}
