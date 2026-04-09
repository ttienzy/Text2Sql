namespace TextToSqlAgent.Application.Agent;

/// <summary>
/// Represents a tool that the agent can invoke during its reasoning loop.
/// Each tool wraps a specific capability (schema lookup, SQL generation, execution, etc.)
/// and provides a description that helps the LLM select the right tool.
/// </summary>
public interface IAgentTool
{
    /// <summary>Human-readable tool name used for logging and LLM tool selection.</summary>
    string Name { get; }

    /// <summary>
    /// Detailed description of what this tool does.
    /// The LLM uses this to decide which tool to invoke.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the tool with the given input and return a result.
    /// The tool reads from and writes to WorkingMemory as needed.
    /// </summary>
    Task<ToolResult> ExecuteAsync(ToolInput input, WorkingMemory memory, CancellationToken ct);
}

/// <summary>
/// Input provided to a tool by the agent's planning step.
/// </summary>
public class ToolInput
{
    /// <summary>The natural language instruction or query for the tool.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Optional parameters the planner passes to the tool.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// Result returned by a tool after execution.
/// </summary>
public class ToolResult
{
    /// <summary>Whether the tool executed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable output that gets added to the agent's observation.</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Optional structured data (e.g., query results, schema info).</summary>
    public object? Data { get; set; }

    /// <summary>Error message if the tool failed.</summary>
    public string? Error { get; set; }

    /// <summary>Name of the tool that produced this result.</summary>
    public string ToolName { get; set; } = string.Empty;

    public static ToolResult Ok(string output, object? data = null) => new()
    {
        Success = true,
        Output = output,
        Data = data
    };

    public static ToolResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        Output = $"Error: {error}"
    };
}
