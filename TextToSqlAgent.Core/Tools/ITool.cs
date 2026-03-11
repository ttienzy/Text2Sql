namespace TextToSqlAgent.Core.Tools;

/// <summary>
/// Represents a tool that the agent can use
/// </summary>
public interface ITool
{
    /// <summary>
    /// Unique name of the tool
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the tool does (for LLM to understand)
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Schema defining the tool's input parameters
    /// </summary>
    ToolSchema Schema { get; }

    /// <summary>
    /// Execute the tool with given input
    /// </summary>
    Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default);
}
