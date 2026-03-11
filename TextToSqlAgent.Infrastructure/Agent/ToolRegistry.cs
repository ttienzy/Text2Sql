using System.Text;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// Registry for managing available tools
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterTool(ITool tool)
    {
        if (_tools.ContainsKey(tool.Name))
        {
            _logger.LogWarning("Tool '{Name}' already registered, overwriting", tool.Name);
        }

        _tools[tool.Name] = tool;
        _logger.LogInformation("Registered tool: {Name}", tool.Name);
    }

    public ITool? GetTool(string name)
    {
        return _tools.GetValueOrDefault(name);
    }

    public List<ITool> GetAllTools()
    {
        return _tools.Values.ToList();
    }

    public string GetToolDescriptions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available Tools:");
        sb.AppendLine();

        foreach (var tool in _tools.Values)
        {
            sb.AppendLine($"Tool: {tool.Name}");
            sb.AppendLine($"Description: {tool.Description}");
            sb.AppendLine($"Parameters: {tool.Schema.ToJsonSchema()}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
