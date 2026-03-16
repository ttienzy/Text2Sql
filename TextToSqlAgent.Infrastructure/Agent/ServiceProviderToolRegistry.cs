using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Tools;
using TextToSqlAgent.Infrastructure.Analysis;
using TextToSqlAgent.Infrastructure.Verification;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// Tool registry that uses IServiceProvider to resolve scoped tools
/// </summary>
public class ServiceProviderToolRegistry : IToolRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceProviderToolRegistry> _logger;
    private readonly Dictionary<string, Type> _toolTypes = new();

    public ServiceProviderToolRegistry(IServiceProvider serviceProvider, ILogger<ServiceProviderToolRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Register known tool types
        RegisterToolType<SchemaExplorerTool>();
        RegisterToolType<SqlGeneratorTool>();
        RegisterToolType<SqlExecutorTool>();
        RegisterToolType<SqlValidatorTool>();
        RegisterToolType<QueryDecomposerTool>();
        RegisterToolType<AmbiguityDetectorTool>();
        RegisterToolType<ComplexityAnalyzerTool>();
        RegisterToolType<ResultVerifierTool>();
    }

    private void RegisterToolType<T>() where T : class, ITool
    {
        // Get tool name by creating a temporary instance
        using var scope = _serviceProvider.CreateScope();
        var tool = scope.ServiceProvider.GetRequiredService<T>();
        _toolTypes[tool.Name] = typeof(T);
        _logger.LogInformation("Registered tool type: {Name} -> {Type}", tool.Name, typeof(T).Name);
    }

    public void RegisterTool(ITool tool)
    {
        // This method is kept for interface compatibility but not used
        // Tools are registered via RegisterToolType<T>()
        _logger.LogWarning("RegisterTool called but ServiceProviderToolRegistry uses type registration");
    }

    public ITool? GetTool(string name)
    {
        if (!_toolTypes.TryGetValue(name, out var toolType))
        {
            return null;
        }

        // Note: The caller is responsible for managing the scope
        // In practice, this will be called within a request scope
        return (ITool)_serviceProvider.GetRequiredService(toolType);
    }

    public List<ITool> GetAllTools()
    {
        var tools = new List<ITool>();

        using var scope = _serviceProvider.CreateScope();
        foreach (var toolType in _toolTypes.Values)
        {
            var tool = (ITool)scope.ServiceProvider.GetRequiredService(toolType);
            tools.Add(tool);
        }

        return tools;
    }

    public string GetToolDescriptions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available Tools:");
        sb.AppendLine();

        using var scope = _serviceProvider.CreateScope();
        foreach (var toolType in _toolTypes.Values)
        {
            var tool = (ITool)scope.ServiceProvider.GetRequiredService(toolType);
            sb.AppendLine($"Tool: {tool.Name}");
            sb.AppendLine($"Description: {tool.Description}");
            sb.AppendLine($"Parameters: {tool.Schema.ToJsonSchema()}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}