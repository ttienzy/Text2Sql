namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Request to the agent
/// </summary>
public class AgentRequest
{
    public string Question { get; set; }
    public string? DatabaseId { get; set; }
    public Dictionary<string, object> Context { get; set; } = new();
    public int MaxSteps { get; set; } = 10;
    public TimeSpan? Timeout { get; set; }

    public AgentRequest(string question, string? databaseId = null)
    {
        Question = question;
        DatabaseId = databaseId;
    }
}
