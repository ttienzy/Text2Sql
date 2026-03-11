namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Observation from executing an action
/// </summary>
public class AgentObservation
{
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    public static AgentObservation FromSuccess(object result)
    {
        return new AgentObservation
        {
            Success = true,
            Result = result
        };
    }

    public static AgentObservation FromError(string errorMessage)
    {
        return new AgentObservation
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
