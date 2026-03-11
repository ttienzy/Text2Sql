using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console;
using System.Text;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Console.Commands;
using TextToSqlAgent.Console.Configuration;
using TextToSqlAgent.Console.Setup;
using TextToSqlAgent.Console.UI;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;

namespace TextToSqlAgent.Console;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure console support
        ConfigureConsoleSupport();

        // Setup Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            // ✅ PHASE 0 FIX: Don't force setup wizard at startup
            // Let user enter command loop first, validate on first query

            IHost host;
            try
            {
                host = CreateHost(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to create application host. Check your configuration.");
                AnsiConsole.MarkupLine("[red]❌ Configuration Error:[/]");
                AnsiConsole.MarkupLine($"[yellow]{ex.Message}[/]");
                AnsiConsole.MarkupLine("\n[cyan]Please check:[/]");
                AnsiConsole.MarkupLine("  • appsettings.json exists and is valid");
                AnsiConsole.MarkupLine("  • Run /config to configure API key and database");
                return 1;
            }

            await RunApplicationAsync(host.Services);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            ConsoleUI.DisplayError(ex);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static IHost CreateHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                ConfigurationLoader.Configure(context, config);
            })
            .ConfigureServices((context, services) =>
            {
                DependencyInjection.ConfigureServices(context.Configuration, services);
            })
            .UseSerilog()
            .Build();
    }

    private static async Task RunApplicationAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        // Get configuration services
        var openAIConfig = scopedServices.GetRequiredService<OpenAIConfig>();
        var dbConfig = scopedServices.GetRequiredService<DatabaseConfig>();
        var agentConfig = scopedServices.GetRequiredService<AgentConfig>();

        try
        {
            // ✅ Display welcome banner immediately
            ConsoleUI.DisplayWelcomeBanner(LLMProvider.OpenAI, openAIConfig.Model);

            // ✅ Check if configuration is complete
            var secureStore = new SecureConfigStore();
            var isConfigured = secureStore.IsConfigured() &&
                              !string.IsNullOrWhiteSpace(openAIConfig.ApiKey);

            if (!isConfigured)
            {
                AnsiConsole.MarkupLine("[yellow]⚠️  Configuration incomplete[/]");
                AnsiConsole.MarkupLine("[dim]Run [cyan]/config[/] to set up API key and database[/]");
                AnsiConsole.WriteLine();
            }
            else
            {
                // Display configuration info if configured
                ConsoleUI.DisplayConfigurationInfo(
                    LLMProvider.OpenAI,
                    openAIConfig.Model,
                    openAIConfig.Temperature,
                    openAIConfig.MaxTokens,
                    agentConfig);
            }

            // ✅ Show help immediately - don't wait for DB
            ConsoleUI.DisplayHelp();

            // ✅ Start interactive loop - validation happens on first query
            await RunInteractiveLoopAsync(scopedServices, isConfigured);

        }
        catch (InvalidOperationException ex)
        {
            Log.Fatal(ex, "Dependency Injection setup failed");
            AnsiConsole.MarkupLine("[red]❌ DI Configuration Error:[/]");
            AnsiConsole.MarkupLine($"[yellow]{ex.Message}[/]");
            AnsiConsole.MarkupLine("\n[cyan]This is likely a coding error. Please check DependencyInjection.cs[/]");
            throw;
        }
    }


    private static async Task RunInteractiveLoopAsync(IServiceProvider services, bool isConfigured)
    {
        var metrics = new Observability.ConsoleMetrics();
        var queryCount = 0;
        bool dbConfigured = false;
        EnhancedAgentOrchestrator? agent = null;
        CommandHandler? commandHandler = null;
        Console.Services.ConsoleRequestProcessor? requestProcessor = null;

        // PHASE 2: Create conversation at session start
        string conversationId = Guid.NewGuid().ToString();

        AnsiConsole.MarkupLine($"[dim]💬 Session ID: {conversationId[..8]}...[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var question = AnsiConsole.Prompt(
                new TextPrompt<string>($"[cyan bold]💬 Question #{queryCount + 1}:[/]")
                    .PromptStyle("white")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(question))
            {
                continue;
            }

            // ✅ PHASE 0 FIX: Lazy validation on first real query
            if (!dbConfigured && !IsCommandOnly(question))
            {
                // Check if we need to configure
                if (!isConfigured)
                {
                    AnsiConsole.MarkupLine("[yellow]⚠️  Please configure API key and database first[/]");
                    AnsiConsole.MarkupLine("[cyan]Run: /config[/]");
                    AnsiConsole.WriteLine();
                    continue;
                }

                // Prompt for database connection
                var dbConfig = services.GetRequiredService<DatabaseConfig>();

                if (string.IsNullOrWhiteSpace(dbConfig.ConnectionString))
                {
                    AnsiConsole.MarkupLine("[yellow]Setting up database connection...[/]");
                    AnsiConsole.WriteLine();

                    // ✅ PromptDatabaseConnection returns non-nullable tuple
                    var (connectionString, connectionName, selectedDbProvider) = ConsoleUI.PromptDatabaseConnection();

                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        AnsiConsole.MarkupLine("[red]❌ No connection string provided[/]");
                        AnsiConsole.WriteLine();
                        continue;
                    }

                    dbConfig.ConnectionString = connectionString;
                    dbConfig.Provider = selectedDbProvider;

                    Log.Information("[Program] Database configured: {Provider}", selectedDbProvider);
                }

                // Test connection
                var sqlExecutor = services.GetRequiredService<SqlExecutor>();
                if (!await TestDatabaseConnectionAsync(sqlExecutor))
                {
                    AnsiConsole.MarkupLine("[red]❌ Cannot connect to database[/]");
                    AnsiConsole.MarkupLine("[cyan]Please check your connection string and try again[/]");
                    AnsiConsole.WriteLine();
                    continue;
                }

                AnsiConsole.MarkupLine($"[green]✓ Database connected successfully[/]");
                AnsiConsole.WriteLine();

                // Initialize agent and services
                Log.Information("[Program] Initializing agent...");
                agent = services.GetRequiredService<EnhancedAgentOrchestrator>();
                commandHandler = new CommandHandler(agent, services);
                requestProcessor = services.GetRequiredService<Console.Services.ConsoleRequestProcessor>();

                dbConfigured = true;
                AnsiConsole.MarkupLine("[green]✓ Agent ready[/]");
                AnsiConsole.WriteLine();
            }

            // Handle commands (some work without DB)
            if (commandHandler == null)
            {
                // Initialize minimal command handler for /config, /help, /exit
                agent = services.GetRequiredService<EnhancedAgentOrchestrator>();
                commandHandler = new CommandHandler(agent, services);
            }

            var (commandResult, newConversationId) = await commandHandler.HandleAsync(question, conversationId);
            conversationId = newConversationId;

            if (commandResult == CommandResult.Exit)
            {
                break;
            }

            if (commandResult == CommandResult.Handled)
            {
                // Check if /config was run and now we're configured
                if (question.Trim().Equals("/config", StringComparison.OrdinalIgnoreCase))
                {
                    var secureStore = new SecureConfigStore();
                    isConfigured = secureStore.IsConfigured();
                }
                continue;
            }

            // Process query - requires DB
            if (!dbConfigured)
            {
                AnsiConsole.MarkupLine("[yellow]⚠️  Database not configured yet[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            queryCount++;
            await ProcessQueryAsync(requestProcessor!, question, queryCount, conversationId, metrics);

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
        }

        // Display session summary
        DisplaySessionSummary(metrics);

        ConsoleUI.DisplayGoodbye();
    }

    private static async Task ProcessQueryAsync(
        Console.Services.ConsoleRequestProcessor processor,
        string question,
        int queryNumber,
        string? conversationId,
        Observability.ConsoleMetrics metrics)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            AgentResponse? response = null;

            // Use cleaner progress indicator - minimal output
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan bold"))
                .StartAsync("[cyan]Processing...[/]", async ctx =>
                {
                    response = await processor.ProcessAsync(question, conversationId);
                });

            var processingTime = DateTime.UtcNow - startTime;

            AnsiConsole.WriteLine();

            // Display agent reasoning steps
            if (response!.ProcessingSteps != null && response.ProcessingSteps.Count > 0)
            {
                AgentStepRenderer.DisplayProcessingSteps(response.ProcessingSteps);
            }

            // Display query explanation if available
            if (!string.IsNullOrEmpty(response.QueryExplanation))
            {
                AgentStepRenderer.DisplayQueryExplanation(response.QueryExplanation);
            }

            // Display self-correction attempts
            if (response.CorrectionHistory != null && response.CorrectionHistory.Count > 0)
            {
                AgentStepRenderer.DisplayCorrectionAttempts(response.CorrectionHistory);
            }

            // Display main response
            ResponseFormatter.Display(response, queryNumber);

            // Display metrics summary
            AgentStepRenderer.DisplayMetricsSummary(
                processingTime,
                response.ProcessingSteps?.Count ?? 0,
                response.CorrectionAttempts,
                response.Success);

            // Record metrics
            metrics.RecordQuery(
                response.Success,
                processingTime,
                response.CorrectionAttempts,
                response.ProcessingSteps?.Count ?? 0);
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            metrics.RecordQuery(false, processingTime, 0, 0);

            AnsiConsole.WriteLine();
            ConsoleUI.DisplayError(ex);
        }
    }

    private static void DisplaySessionSummary(Observability.ConsoleMetrics metrics)
    {
        var summary = metrics.GetSummary();

        if (summary.TotalQueries == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow bold]📊 SESSION SUMMARY[/]").RuleStyle("yellow"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Total Queries", summary.TotalQueries.ToString());
        table.AddRow("Successful", $"[green]{summary.SuccessfulQueries}[/]");
        table.AddRow("Failed", summary.FailedQueries > 0 ? $"[red]{summary.FailedQueries}[/]" : "0");
        table.AddRow("Success Rate", $"{summary.SuccessRate:P0}");
        table.AddRow("Avg Processing Time", $"{summary.AverageProcessingTime.TotalSeconds:F2}s");
        table.AddRow("Max Processing Time", $"{summary.MaxProcessingTime.TotalSeconds:F2}s");
        table.AddRow("Avg Corrections", $"{summary.AverageCorrectionAttempts:F1}");
        table.AddRow("Avg Steps", $"{summary.AverageSteps:F1}");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Check if input is a command that doesn't require database
    /// </summary>
    private static bool IsCommandOnly(string input)
    {
        var trimmed = input.Trim().ToLowerInvariant();
        return trimmed.StartsWith("/help") ||
               trimmed.StartsWith("/exit") ||
               trimmed.StartsWith("/quit") ||
               trimmed.StartsWith("/config") ||
               trimmed.StartsWith("/clear");
    }

    /// <summary>
    /// Set up Vietnamese language support in the console.
    /// </summary>
    private static void ConfigureConsoleSupport()
    {
        try
        {
            // Thiết lập UTF-8 encoding cho Console
            System.Console.OutputEncoding = Encoding.UTF8;
            System.Console.InputEncoding = Encoding.UTF8;

            // Set culture to invariant or US English for consistent formatting
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Windows-specific: Thiết lập Code Page 65001 (UTF-8)
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // Thử thiết lập code page cho Windows
                    System.Console.OutputEncoding = Encoding.GetEncoding(65001);
                }
                catch
                {
                    // Nếu lỗi, giữ nguyên UTF-8 mặc định
                }
            }

            Log.Debug("Console support configured successfully");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not fully configure console support, but continuing...");
        }
    }

    private static async Task<bool> TestDatabaseConnectionAsync(SqlExecutor executor)
    {
        Log.Information("[Program] Starting database connection test...");
        AnsiConsole.MarkupLine("[yellow]Testing database connection...[/]");

        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Connecting to database...[/]", async ctx =>
                {
                    Log.Debug("[Program] Calling executor.ValidateConnectionAsync()...");
                    var connectionResult = await executor.ValidateConnectionAsync();
                    Log.Information("[Program] Connection test result: {Result}", connectionResult);
                    return connectionResult;
                });

            if (result)
            {
                Log.Information("[Program] Database connection successful");
            }
            else
            {
                Log.Warning("[Program] Database connection failed");
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Program] Database connection test failed with exception");
            AnsiConsole.MarkupLine($"[red]❌ Connection test failed: {ex.Message}[/]");
            return false;
        }
    }
}