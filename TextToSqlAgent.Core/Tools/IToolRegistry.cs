namespace TextToSqlAgent.Core.Tools;

/// <summary>
/// Registry for managing available tools
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Register a tool
    /// </summary>
    void RegisterTool(ITool tool);

    /// <summary>
    /// Get a tool by name
    /// </summary>
    ITool? GetTool(string name);

    /// <summary>
    /// Get all available tools
    /// </summary>
    List<ITool> GetAllTools();

    /// <summary>
    /// Get tool descriptions for LLM (formatted for prompt)
    /// </summary>
    string GetToolDescriptions();
}
