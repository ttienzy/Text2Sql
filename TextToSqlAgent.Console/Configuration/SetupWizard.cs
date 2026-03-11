using Spectre.Console;

namespace TextToSqlAgent.Console.Configuration;

/// <summary>
/// Interactive setup wizard for first-time configuration
/// </summary>
public class SetupWizard
{
    private readonly SecureConfigStore _configStore;

    public SetupWizard()
    {
        _configStore = new SecureConfigStore();
    }

    /// <summary>
    /// Run the setup wizard
    /// </summary>
    public bool Run(bool isReconfigure = false)
    {
        AnsiConsole.Clear();
        DisplayWelcome(isReconfigure);

        // Step 1: OpenAI API Key
        var apiKey = PromptApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            AnsiConsole.MarkupLine("[yellow]Setup cancelled.[/]");
            return false;
        }

        // Save configuration
        var config = new SecureConfigStore.SecureConfig
        {
            OpenAIApiKey = apiKey
        };

        try
        {
            _configStore.SaveConfig(config);
            DisplaySuccess();
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to save configuration: {ex.Message}[/]");
            return false;
        }
    }

    private void DisplayWelcome(bool isReconfigure)
    {
        var title = isReconfigure ? "🔧 Reconfigure TextToSqlAgent" : "🚀 Welcome to TextToSqlAgent";
        var subtitle = isReconfigure
            ? "Update your configuration"
            : "Let's get you set up in just a few steps";

        var panel = new Panel(
            new Markup($"[bold cyan]{title}[/]\n\n[dim]{subtitle}[/]"))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan),
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        if (!isReconfigure)
        {
            AnsiConsole.MarkupLine("[dim]This wizard will help you configure:[/]");
            AnsiConsole.MarkupLine("  [cyan]•[/] OpenAI API Key (required)");
            AnsiConsole.WriteLine();
        }
    }

    private string PromptApiKey()
    {
        AnsiConsole.MarkupLine("[bold yellow]Step 1: OpenAI API Key[/]");
        AnsiConsole.MarkupLine("[dim]Your API key will be stored securely and encrypted.[/]");
        AnsiConsole.WriteLine();

        // Check if environment variable exists
        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(envApiKey))
        {
            AnsiConsole.MarkupLine("[green]✓ Found OPENAI_API_KEY in environment variables[/]");

            var useEnv = AnsiConsole.Confirm(
                "[yellow]Use this API key from environment?[/]",
                defaultValue: true);

            if (useEnv)
            {
                return envApiKey;
            }
        }

        // Manual input
        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Enter your OpenAI API Key:[/]")
                .PromptStyle("green")
                .Secret()
                .Validate(key =>
                {
                    if (string.IsNullOrWhiteSpace(key))
                        return ValidationResult.Error("[red]API key cannot be empty[/]");

                    if (!key.StartsWith("sk-"))
                        return ValidationResult.Error("[red]OpenAI API keys typically start with 'sk-'[/]");

                    if (key.Length < 20)
                        return ValidationResult.Error("[red]API key seems too short[/]");

                    return ValidationResult.Success();
                }));

        AnsiConsole.WriteLine();

        // Confirm
        var confirm = AnsiConsole.Confirm(
            "[yellow]Save this API key?[/]",
            defaultValue: true);

        return confirm ? apiKey : string.Empty;
    }

    private void DisplaySuccess()
    {
        AnsiConsole.WriteLine();

        var successPanel = new Panel(
            new Markup(
                "[green bold]✓ Configuration Saved Successfully![/]\n\n" +
                "[dim]Your settings have been encrypted and stored securely at:[/]\n" +
                $"[cyan]{_configStore.GetConfigDirectory()}[/]\n\n" +
                "[dim]You can reconfigure anytime using the[/] [cyan]/config[/] [dim]command.[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(successPanel);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Press any key to continue...[/]");
        System.Console.ReadKey(true);
    }

    /// <summary>
    /// Display configuration info
    /// </summary>
    public void DisplayCurrentConfig()
    {
        var config = _configStore.LoadConfig();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue);

        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Status[/]");

        var apiKeyStatus = !string.IsNullOrEmpty(config.OpenAIApiKey)
            ? $"[green]✓ Configured[/] [dim](sk-...{config.OpenAIApiKey[^4..]})[/]"
            : "[red]✗ Not configured[/]";

        table.AddRow("OpenAI API Key", apiKeyStatus);
        table.AddRow("Last Updated", config.IsConfigured
            ? $"[cyan]{config.LastUpdated:yyyy-MM-dd HH:mm:ss}[/]"
            : "[dim]Never[/]");
        table.AddRow("Config Location", $"[dim]{_configStore.GetConfigDirectory()}[/]");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("⚙️  Current Configuration", Justify.Left),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Reset configuration
    /// </summary>
    public bool ResetConfig()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]⚠️  Reset Configuration[/]");
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm(
            "[red]Are you sure you want to delete all saved configuration?[/]",
            defaultValue: false);

        if (confirm)
        {
            try
            {
                _configStore.ClearConfig();
                AnsiConsole.MarkupLine("[green]✓ Configuration reset successfully[/]");
                AnsiConsole.WriteLine();
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to reset: {ex.Message}[/]");
                return false;
            }
        }

        AnsiConsole.MarkupLine("[yellow]Reset cancelled[/]");
        return false;
    }
}
