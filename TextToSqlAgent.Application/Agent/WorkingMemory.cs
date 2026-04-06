using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Agent;

/// <summary>
/// Structured working memory for the agent's reasoning loop.
/// Accumulates thoughts, tool calls, observations, and schema context across steps.
/// </summary>
public class WorkingMemory
{
    /// <summary>Original user question.</summary>
    public string OriginalQuestion { get; set; } = string.Empty;

    /// <summary>Conversation ID for multi-turn support.</summary>
    public string? ConversationId { get; set; }

    /// <summary>Ordered list of reasoning steps (thought → action → observation).</summary>
    public List<ReasoningStep> Steps { get; set; } = new();

    /// <summary>Schema context accumulated by tools.</summary>
    public RetrievedSchemaContext? SchemaContext { get; set; }

    /// <summary>Full database schema (if loaded).</summary>
    public DatabaseSchema? FullSchema { get; set; }

    /// <summary>Generated SQL (updated by SQL generation tool).</summary>
    public string? GeneratedSql { get; set; }

    /// <summary>SQL execution result (updated by execution tool).</summary>
    public SqlExecutionResult? ExecutionResult { get; set; }

    /// <summary>Final answer produced by the agent.</summary>
    public string? FinalAnswer { get; set; }

    /// <summary>Whether the agent has reached a final answer.</summary>
    public bool IsResolved { get; set; }

    /// <summary>Accumulated errors for recovery reasoning.</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Tables discovered during reasoning.</summary>
    public HashSet<string> DiscoveredTables { get; set; } = new();

    /// <summary>Current step number (1-indexed).</summary>
    public int CurrentStep => Steps.Count;

    /// <summary>SSE progress reporter (optional).</summary>
    public IProgress<AgentStageEvent>? Progress { get; set; }

    /// <summary>SQL token streaming callback (optional).</summary>
    public Action<string>? SqlTokenCallback { get; set; }

    /// <summary>Add a reasoning step to the trace.</summary>
    public void AddStep(ReasoningStep step)
    {
        step.StepNumber = Steps.Count + 1;
        step.Timestamp = DateTime.UtcNow;
        Steps.Add(step);
    }

    /// <summary>Build a summary of reasoning history for the LLM's next planning step.</summary>
    public string BuildReasoningSummary()
    {
        if (Steps.Count == 0) return "No previous reasoning steps.";

        var lines = new List<string> { $"Question: {OriginalQuestion}\n" };

        foreach (var step in Steps)
        {
            lines.Add($"Step {step.StepNumber}:");
            if (!string.IsNullOrEmpty(step.Thought))
                lines.Add($"  Thought: {step.Thought}");
            if (!string.IsNullOrEmpty(step.Action))
                lines.Add($"  Action: {step.Action}");
            if (!string.IsNullOrEmpty(step.Observation))
                lines.Add($"  Observation: {step.Observation}");
            lines.Add("");
        }

        if (Errors.Count > 0)
        {
            lines.Add($"Errors encountered: {string.Join("; ", Errors)}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Report progress via SSE if a progress reporter is attached.</summary>
    public void ReportProgress(AgentStage stage, string message, double progress, string? detail = null)
    {
        Progress?.Report(new AgentStageEvent
        {
            Stage = stage,
            Message = message,
            Progress = progress,
            Detail = detail
        });
    }
}

/// <summary>
/// A single step in the agent's reasoning trace (thought-action-observation triple).
/// </summary>
public class ReasoningStep
{
    public int StepNumber { get; set; }

    /// <summary>The agent's reasoning about what to do next.</summary>
    public string? Thought { get; set; }

    /// <summary>The action taken (tool name + input summary).</summary>
    public string? Action { get; set; }

    /// <summary>The observation from the tool's result.</summary>
    public string? Observation { get; set; }

    /// <summary>Tool result data (optional, for structured access).</summary>
    public ToolResult? ToolResult { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
