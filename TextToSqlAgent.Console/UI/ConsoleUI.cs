using Spectre.Console;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Console.Configuration;

namespace TextToSqlAgent.Console.UI;

public static class ConsoleUI
{
    public static void DisplayWelcomeBanner(LLMProvider provider, string modelName)
    {
        AnsiConsole.Clear();

        var rule = new Rule("[blue bold]TEXT TO SQL AGENT[/]");
        rule.Justification = Justify.Center;
        AnsiConsole.Write(rule);

        AnsiConsole.WriteLine();

        var providerDisplay = provider == LLMProvider.OpenAI ? "OpenAI" : "Gemini";
        var grid = new Grid();
        grid.AddColumn();
        grid.AddRow($"[dim]Powered by:[/] [cyan]{providerDisplay} - {modelName}[/]");
        grid.AddRow("[dim]Version:[/] [green]1.0.0 (Week 1-2 MVP)[/]");
        grid.AddRow("[dim]Author:[/] [yellow]Text To SQL Team[/]");

        AnsiConsole.Write(Align.Center(grid));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public static void DisplayConfigurationInfo(LLMProvider provider, string model, double temperature, int maxTokens, AgentConfig agentConfig)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Grey));
        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Value[/]");

        var providerName = provider == LLMProvider.OpenAI ? "OpenAI" : "Gemini";
        table.AddRow("LLM Provider", $"[cyan]{providerName}[/]");
        table.AddRow("LLM Model", $"[cyan]{model}[/]");
        table.AddRow("Temperature", $"[cyan]{temperature}[/]");
        table.AddRow("Max Tokens", $"[cyan]{maxTokens}[/]");
        table.AddRow("Max Self-Correction", $"[cyan]{agentConfig.MaxSelfCorrectionAttempts}[/]");
        table.AddRow("SQL Explanation", $"[cyan]{(agentConfig.EnableSQLExplanation ? "Enabled" : "Disabled")}[/]");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("⚙️  Configuration", Justify.Left),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static (string connectionString, string connectionName, Core.Enums.DatabaseProvider provider) PromptDatabaseConnection()
    {
        AnsiConsole.MarkupLine("[yellow]📊 Database Connection Setup[/]");
        AnsiConsole.WriteLine();

        var connectionManager = new ConnectionManager();
        var data = connectionManager.LoadConnections();

        // Build main menu choices
        var choices = new List<string>();

        // Add saved connections
        if (data.Connections.Any())
        {
            foreach (var conn in data.Connections.OrderByDescending(c => c.LastUsed))
            {
                var marker = conn.Name == data.LastUsedConnectionName ? " [green](last used)[/]" : "";
                choices.Add($"📁 {conn.Name}{marker}");
            }
            choices.Add(""); // Separator
        }

        // Add builder option
        choices.Add("[cyan]🔧 Build New Connection (Step-by-Step)[/]");
        
        // Add delete option if connections exist
        if (data.Connections.Any())
        {
            choices.Add("[red]🗑️  Delete Saved Connection[/]");
        }

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Choose how to connect:[/]")
                .PageSize(15)
                .AddChoices(choices.Where(c => !string.IsNullOrEmpty(c))));

        string connectionString;
        string connectionName;
        var provider = Core.Enums.DatabaseProvider.SqlServer; // Default

        // Check if user wants to delete a connection
        if (selection == "[red]🗑️  Delete Saved Connection[/]")
        {
            return HandleDeleteConnection(connectionManager, data);
        }

        // Check if user selected an existing saved connection
        if (selection.StartsWith("📁 "))
        {
            // User selected an existing saved connection
            var selectedName = selection
                .Replace("📁 ", "")
                .Replace(" [green](last used)[/]", "");

            var savedConnection = data.Connections.First(c => c.Name == selectedName);

            connectionString = savedConnection.ConnectionString;
            connectionName = savedConnection.Name;
            provider = savedConnection.Provider; // Load saved provider

            // Update last used
            savedConnection.LastUsed = DateTime.Now;
            data.LastUsedConnectionName = selectedName;
            connectionManager.SaveConnections(data);

            AnsiConsole.MarkupLine($"[green]✓ Loaded saved connection ({provider})[/]");
        }
        else
        {
            // Use interactive builder
            AnsiConsole.WriteLine();
            var (builtConnection, serverName, databaseName, selectedProvider) = ConnectionBuilder.BuildConnectionString();

            if (string.IsNullOrEmpty(builtConnection))
            {
                // User cancelled, restart the flow
                AnsiConsole.MarkupLine("[yellow]← Returning to connection menu...[/]");
                AnsiConsole.WriteLine();
                return PromptDatabaseConnection();
            }

            connectionString = builtConnection;
            provider = selectedProvider;

            var saveOption = AnsiConsole.Confirm(
                "[yellow]💾 Save this connection for future use?[/]",
                defaultValue: true);

            if (saveOption)
            {
                connectionName = AnsiConsole.Prompt(
                    new TextPrompt<string>("[yellow]Enter a name for this connection:[/]")
                        .PromptStyle("green")
                        .DefaultValue($"{provider} - {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .ValidationErrorMessage("[red]Name cannot be empty[/]")
                        .Validate(s => !string.IsNullOrWhiteSpace(s)));

                connectionManager.AddOrUpdateConnection(data, connectionName, connectionString, provider);
                AnsiConsole.MarkupLine("[green]✓ Connection saved![/]");
            }
            else
            {
                connectionName = $"Temp {provider} Connection {DateTime.Now:HH:mm:ss}";
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Selected:[/] [cyan]{connectionName}[/]");
        AnsiConsole.MarkupLine($"[dim]Provider:[/] [cyan]{provider}[/]");
        AnsiConsole.MarkupLine($"[dim]Info:[/] [grey]{ConnectionManager.MaskConnectionString(connectionString)}[/]");
        AnsiConsole.WriteLine();

        return (connectionString, connectionName, provider);
    }

    private static (string connectionString, string connectionName, Core.Enums.DatabaseProvider provider) HandleDeleteConnection(
        ConnectionManager connectionManager, 
        ConnectionManager.ConnectionsData data)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]🗑️  Delete Saved Connection[/]");
        AnsiConsole.WriteLine();

        var connectionNames = data.Connections.Select(c => c.Name).ToList();
        connectionNames.Add("← Cancel");

        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[red]Select connection to delete:[/]")
                .PageSize(15)
                .AddChoices(connectionNames));

        if (selectedName == "← Cancel")
        {
            return PromptDatabaseConnection();
        }

        var confirm = AnsiConsole.Confirm($"[red]Are you sure you want to delete '{selectedName}'?[/]", false);

        if (confirm)
        {
            data.Connections.RemoveAll(c => c.Name == selectedName);
            connectionManager.SaveConnections(data);
            AnsiConsole.MarkupLine($"[green]✓ Deleted connection '{selectedName}'[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Deletion cancelled[/]");
        }

        AnsiConsole.WriteLine();
        return PromptDatabaseConnection();
    }


    public static void DisplayHelp()
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Blue));
        table.AddColumn(new TableColumn("[bold yellow]Command[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold yellow]Description[/]").LeftAligned());

        // ═══ BASIC COMMANDS ═══
        table.AddRow("[green bold]═══ BASIC ═══[/]", "");
        table.AddRow("[cyan]help[/], [cyan]?[/]", "Show help");
        table.AddRow("[cyan]examples[/]", "Show example questions");
        table.AddRow("[cyan]clear[/], [cyan]cls[/]", "Clear screen");

        table.AddEmptyRow();

        // ═══ SCHEMA & INDEX ═══
        table.AddRow("[green bold]═══ SCHEMA & INDEX ═══[/]", "");
        table.AddRow("[cyan]index[/]", "Index database schema into vector DB");
        table.AddRow("[cyan]reindex[/]", "Clear and re-index full schema");
        table.AddRow("[cyan]check index[/]", "Check current index status");
        table.AddRow("[cyan]clear cache[/]", "Clear schema cache");

        table.AddEmptyRow();

        // ═══ DEBUG & TROUBLESHOOTING ═══
        table.AddRow("[yellow bold]═══ DEBUG & TROUBLESHOOTING ═══[/]", "");
        table.AddRow("[cyan]debug[/]", "[green]🔧[/] Diagnose Qdrant (connection, config, data)");
        table.AddRow("[cyan]recreate[/]", "[red]⚠️[/] Delete and recreate Qdrant collection");

        table.AddEmptyRow();

        // ═══ DATABASE ═══
        table.AddRow("[blue bold]═══ DATABASE ═══[/]", "");
        table.AddRow("[cyan]show db[/]", "Show current database connection");
        table.AddRow("[cyan]switch db[/]", "Switch to another database");

        table.AddEmptyRow();

        // ═══ EXIT ═══
        table.AddRow("[cyan]exit[/], [cyan]quit[/]", "Exit program");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("📚 AVAILABLE COMMANDS", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // ═══ TIPS ═══
        var tipsPanel = new Panel(
            new Markup(
                "[dim]💡 Tips:[/]\n" +
                "  • If answers are inaccurate, try [cyan]'reindex'[/] to refresh schema\n" +
                "  • If Qdrant connection fails, run [cyan]'debug'[/] to diagnose\n" +
                "  • Vector size mismatch? Run [cyan]'recreate'[/] then [cyan]'index'[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };

        AnsiConsole.Write(tipsPanel);
        AnsiConsole.WriteLine();
    }

    public static void DisplayExamples()
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Green));
        table.AddColumn("[bold]Category[/]");
        table.AddColumn("[bold]Sample Question[/]");

        table.AddRow("[yellow]Schema[/]", "How many tables are in the database?");
        table.AddRow("[yellow]Count[/]", "How many customers do we have?");
        table.AddRow("[yellow]List[/]", "List all customers");
        table.AddRow("[yellow]Filter[/]", "Which customers are in Hanoi?");
        table.AddRow("[yellow]Aggregate[/]", "Top 5 best-selling products");
        table.AddRow("[yellow]Date Range[/]", "Orders this week");
        table.AddRow("[yellow]Join[/]", "Orders for customer Nguyen Van A");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("💡 Sample Questions", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void DisplayError(Exception ex)
    {
        var panel = new Panel(new Markup($"[red]{ex.Message}[/]"))
        {
            Header = new PanelHeader("❌ Unexpected Error", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red)
        };

        AnsiConsole.Write(panel);

        if (ex.InnerException != null)
        {
            AnsiConsole.MarkupLine($"[dim]Details: {ex.InnerException.Message}[/]");
        }

        AnsiConsole.WriteLine();
    }

    public static void DisplayGoodbye()
    {
        AnsiConsole.WriteLine();

        var panel = new Panel(
            Align.Center(
                new Markup("[green bold]Thank you for using Text To SQL Agent!\n\n[dim]Happy coding! 🚀[/]"),
                VerticalAlignment.Middle))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}