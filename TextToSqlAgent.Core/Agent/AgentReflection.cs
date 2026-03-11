namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Agent's reflection on the observation
/// </summary>
public class AgentReflection
{
    public string Assessment { get; set; } = string.Empty;
    public bool ShouldTerminate { get; set; }
    public string? TerminationReason { get; set; }
    public string? NextAction { get; set; }
    public double Confidence { get; set; } // 0.0 to 1.0
}
