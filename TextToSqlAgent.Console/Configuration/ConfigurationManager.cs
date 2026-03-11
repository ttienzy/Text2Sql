using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace TextToSqlAgent.Console.Configuration;

/// <summary>
/// Enterprise-grade configuration manager
/// Centralized configuration loading with clear priority and validation
/// </summary>
public class AppConfigurationManager
{
    private readonly SecureConfigStore _secureStore;
    private readonly IConfiguration _configuration;

    public AppConfigurationManager(IConfiguration configuration)
    {
        _configuration = configuration;
        _secureStore = new SecureConfigStore();
    }

    /// <summary>
    /// Load OpenAI API key with explicit priority
    /// Returns the key and the source it was loaded from
    /// </summary>
    public (string apiKey, string source) LoadOpenAIApiKey()
    {
        // Priority 1: Secure Store (highest priority)
        var secureConfig = _secureStore.LoadConfig();
        if (secureConfig.IsConfigured && !string.IsNullOrEmpty(secureConfig.OpenAIApiKey))
        {
            return (secureConfig.OpenAIApiKey, "Secure Store");
        }

        // Priority 2: Environment Variable
        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            return (envKey, "Environment Variable");
        }

        // Priority 3: Configuration (appsettings.json)
        var configKey = _configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(configKey))
        {
            return (configKey, "appsettings.json");
        }

        // Not found
        return (string.Empty, "Not Found");
    }

    /// <summary>
    /// Validate API key format
    /// </summary>
    public bool ValidateApiKeyFormat(string apiKey, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            errorMessage = "API key is empty";
            return false;
        }

        if (!apiKey.StartsWith("sk-"))
        {
            errorMessage = $"API key should start with 'sk-', but starts with '{apiKey.Substring(0, Math.Min(5, apiKey.Length))}'";
            return false;
        }

        if (apiKey.Length < 20)
        {
            errorMessage = $"API key seems too short (length: {apiKey.Length})";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Display configuration status
    /// </summary>
    public void DisplayConfigurationStatus()
    {
        var (apiKey, source) = LoadOpenAIApiKey();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue);

        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Status[/]");

        if (!string.IsNullOrEmpty(apiKey))
        {
            var isValid = ValidateApiKeyFormat(apiKey, out var errorMessage);
            var statusColor = isValid ? "green" : "yellow";
            var statusText = isValid ? "✓ Configured" : $"⚠ {errorMessage}";

            table.AddRow(
                "OpenAI API Key",
                $"[{statusColor}]{statusText}[/] [dim]({MaskApiKey(apiKey)})[/]"
            );
            table.AddRow("Source", $"[cyan]{source}[/]");
        }
        else
        {
            table.AddRow("OpenAI API Key", "[red]✗ Not configured[/]");
            table.AddRow("Source", "[dim]None[/]");
        }

        var secureConfig = _secureStore.LoadConfig();
        table.AddRow("Last Updated", secureConfig.IsConfigured
            ? $"[cyan]{secureConfig.LastUpdated:yyyy-MM-dd HH:mm:ss}[/]"
            : "[dim]Never[/]");
        table.AddRow("Config Location", $"[dim]{_secureStore.GetConfigDirectory()}[/]");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("⚙️  Current Configuration", Justify.Left),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Save API key to secure store
    /// </summary>
    public bool SaveApiKey(string apiKey)
    {
        try
        {
            var config = new SecureConfigStore.SecureConfig
            {
                OpenAIApiKey = apiKey
            };

            _secureStore.SaveConfig(config);
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to save API key: {ex.Message}[/]");
            return false;
        }
    }

    /// <summary>
    /// Clear all configuration
    /// </summary>
    public bool ClearConfiguration()
    {
        try
        {
            _secureStore.ClearConfig();
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to clear configuration: {ex.Message}[/]");
            return false;
        }
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
            return "***";

        return $"{apiKey.Substring(0, 7)}...{apiKey.Substring(apiKey.Length - 4)}";
    }
}
