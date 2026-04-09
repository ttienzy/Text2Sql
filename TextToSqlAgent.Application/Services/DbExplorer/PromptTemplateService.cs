using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using System.Text.Json;
using TextToSqlAgent.Application.Options;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Service for loading and rendering prompt templates using Semantic Kernel
/// </summary>
public class PromptTemplateService
{
    private readonly ILogger<PromptTemplateService> _logger;
    private readonly DbExplorerOptions _options;
    private readonly Dictionary<string, string> _promptCache = new();
    private readonly Dictionary<string, PromptConfig> _configCache = new();

    public PromptTemplateService(
        ILogger<PromptTemplateService> logger,
        IOptions<DbExplorerOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    private string? ResolveFilePath(string filename)
    {
        var basePath = _options.AI.Prompts.BasePath; // e.g. "Prompts/DbExplorer"
        
        var possibleDirs = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var startDir in possibleDirs)
        {
            var currentDir = new DirectoryInfo(startDir);
            while (currentDir != null)
            {
                var combinedPath = Path.Combine(currentDir.FullName, basePath, filename);
                if (File.Exists(combinedPath))
                {
                    return Path.GetFullPath(combinedPath);
                }
                currentDir = currentDir.Parent;
            }
        }

        // Direct fallback
        var directPath = Path.Combine(basePath, filename);
        if (File.Exists(directPath)) return Path.GetFullPath(directPath);

        return null;
    }

    /// <summary>
    /// Load prompt template from file
    /// </summary>
    public async Task<string> LoadPromptAsync(string promptName)
    {
        if (_promptCache.TryGetValue(promptName, out var cached))
        {
            return cached;
        }

        var promptPath = ResolveFilePath($"{promptName}.skprompt.txt");

        if (promptPath == null)
        {
            _logger.LogWarning("[PromptTemplate] Prompt file not found for: {PromptName}", promptName);
            throw new FileNotFoundException($"Prompt template not found: {promptName}");
        }

        var content = await File.ReadAllTextAsync(promptPath);
        _promptCache[promptName] = content;

        _logger.LogInformation("[PromptTemplate] Loaded prompt: {PromptName}", promptName);
        return content;
    }

    /// <summary>
    /// Load prompt configuration
    /// </summary>
    public async Task<PromptConfig> LoadConfigAsync(string promptName)
    {
        if (_configCache.TryGetValue(promptName, out var cached))
        {
            return cached;
        }

        var configPath = ResolveFilePath("config.json");

        if (configPath == null)
        {
            _logger.LogWarning("[PromptTemplate] Config file not found, using defaults");
            return GetDefaultConfig();
        }

        var json = await File.ReadAllTextAsync(configPath);
        var allConfigs = JsonSerializer.Deserialize<Dictionary<string, PromptConfig>>(json);

        if (allConfigs != null && allConfigs.TryGetValue(promptName, out var config))
        {
            _configCache[promptName] = config;
            return config;
        }

        return GetDefaultConfig();
    }

    /// <summary>
    /// Render prompt with variables
    /// </summary>
    public string RenderPrompt(string template, Dictionary<string, string> variables)
    {
        var rendered = template;

        foreach (var (key, value) in variables)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", value);
        }

        return rendered;
    }

    /// <summary>
    /// Get prompt with config for LLM call
    /// </summary>
    public async Task<(string prompt, PromptConfig config)> GetPromptWithConfigAsync(
        string promptName,
        Dictionary<string, string> variables)
    {
        var template = await LoadPromptAsync(promptName);
        var config = await LoadConfigAsync(promptName);
        var rendered = RenderPrompt(template, variables);

        return (rendered, config);
    }

    private PromptConfig GetDefaultConfig()
    {
        return new PromptConfig
        {
            Temperature = _options.AI.Prompts.Temperature,
            MaxTokens = _options.AI.Prompts.MaxTokens,
            TopP = 0.9
        };
    }

    /// <summary>
    /// Clear cache (useful for hot-reload)
    /// </summary>
    public void ClearCache()
    {
        _promptCache.Clear();
        _configCache.Clear();
        _logger.LogInformation("[PromptTemplate] Cache cleared");
    }
}

/// <summary>
/// Prompt configuration model
/// </summary>
public class PromptConfig
{
    public double Temperature { get; set; }
    public int MaxTokens { get; set; }
    public double TopP { get; set; }
}
