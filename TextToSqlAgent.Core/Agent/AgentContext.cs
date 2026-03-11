namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Context maintained throughout agent execution
/// </summary>
public class AgentContext
{
    public AgentRequest Request { get; set; }
    public AgentState State { get; set; }
    public Dictionary<string, object> WorkingMemory { get; set; } = new();

    public AgentContext(AgentRequest request)
    {
        Request = request;
        State = new AgentState
        {
            Status = "Idle",
            StartTime = DateTime.UtcNow
        };
    }

    public void AddStep(AgentStep step)
    {
        State.History.Add(step);
        State.CurrentStep = step.StepNumber;
    }

    public bool IsComplete => State.Status == "Complete" || State.Status == "Failed";

    public int Steps => State.History.Count;

    public AgentResult ToResult()
    {
        var result = new AgentResult
        {
            Success = State.Status == "Complete",
            Steps = State.History,
            TotalSteps = State.History.Count,
            TotalLatencyMs = (long)(State.EndTime ?? DateTime.UtcNow).Subtract(State.StartTime).TotalMilliseconds,
            TotalTokensUsed = State.History.Sum(s => s.TokensUsed)
        };

        // Extract answer and SQL from working memory
        if (WorkingMemory.ContainsKey("answer"))
            result.Answer = WorkingMemory["answer"]?.ToString();

        if (WorkingMemory.ContainsKey("sql"))
            result.SqlGenerated = WorkingMemory["sql"]?.ToString();

        if (WorkingMemory.TryGetValue("query_result", out var queryResult))
            result.QueryResult = queryResult;
        else if (WorkingMemory.TryGetValue("query_results", out var queryResults))
            result.QueryResult = queryResults;

        if (State.Status == "Failed" && WorkingMemory.ContainsKey("error"))
            result.ErrorMessage = WorkingMemory["error"]?.ToString();

        // Processing steps for compatibility
        result.ProcessingSteps = State.History.Select(s =>
            $"Step {s.StepNumber}: {s.Thought} → {s.Action?.ToolName}").ToList();

        return result;
    }

    public string GetHistorySummary()
    {
        var summary = $"Question: {Request.Question}\n\n";
        summary += $"Steps completed: {State.History.Count}\n\n";

        foreach (var step in State.History)
        {
            summary += $"Step {step.StepNumber}:\n";
            summary += $"  Thought: {step.Thought}\n";
            summary += $"  Action: {step.Action?.ToolName}\n";
            summary += $"  Result: {(step.Observation?.Success == true ? "Success" : "Failed")}\n";
            summary += "\n";
        }

        return summary;
    }
}
