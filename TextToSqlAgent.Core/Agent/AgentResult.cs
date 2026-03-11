namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Result from agent execution
/// </summary>
public class AgentResult
{
    public bool Success { get; set; }
    public string? Answer { get; set; }
    public string? SqlGenerated { get; set; }
    public List<AgentStep> Steps { get; set; } = new();
    public int TotalSteps { get; set; }
    public long TotalLatencyMs { get; set; }
    public int TotalTokensUsed { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    // For compatibility with old AgentResponse
    public object? QueryResult { get; set; }
    public List<string> ProcessingSteps { get; set; } = new();
    public bool FromCache { get; set; }
}
