using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console;
using System.Text;
using TextToSqlAgent.Console.Agent;
using TextToSqlAgent.Console.Commands;
using TextToSqlAgent.Console.Setup;
using TextToSqlAgent.Console.UI;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;

namespace TextToSqlAgent.Console;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Vietnamese language support
        ConfigureVietnameseSupport();

        // Setup Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            ConsoleUI.DisplayWelcomeBanner();

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
                AnsiConsole.MarkupLine("  • User secrets are configured (dotnet user-secrets set ...)");
                AnsiConsole.MarkupLine("  • All required config values are present");
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

        // Get services
        var geminiConfig = scopedServices.GetRequiredService<GeminiConfig>();
        var dbConfig = scopedServices.GetRequiredService<DatabaseConfig>();
        var agentConfig = scopedServices.GetRequiredService<AgentConfig>();
        var agent = scopedServices.GetRequiredService<TextToSqlAgentOrchestrator>();
        var sqlExecutor = scopedServices.GetRequiredService<SqlExecutor>();
        try
        {
            // Display configuration
            ConsoleUI.DisplayConfigurationInfo(geminiConfig, agentConfig);

            // Get database connection
            // Get database connection with error handling
            (string connectionString, string connectionName)? connectionInfo = null;

            try
            {
                connectionInfo = ConsoleUI.PromptDatabaseConnection();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get database connection");
                AnsiConsole.MarkupLine("[red]❌ Error getting database connection:[/]");
                AnsiConsole.MarkupLine($"[yellow]{ex.Message}[/]");
                return;
            }

            if (connectionInfo == null)
            {
                AnsiConsole.MarkupLine("[red]❌ No connection string provided. Exiting...[/]");
                return;
            }

            var (connectionString, connectionName) = connectionInfo.Value;
            dbConfig.ConnectionString = connectionString;

            // Test connection
            if (!await TestDatabaseConnectionAsync(sqlExecutor))
            {
                AnsiConsole.MarkupLine("[red]❌ Cannot connect to database. Exiting...[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[green]✓ Connected to:[/] [cyan]{connectionName}[/]");
            AnsiConsole.WriteLine();

            // Show help
            ConsoleUI.DisplayHelp();

            // Start interactive loop
            await RunInteractiveLoopAsync(agent, scopedServices);

        }
        catch (InvalidOperationException ex)
        {
            Log.Fatal(ex, "Dependency Injection setup failed");
            AnsiConsole.MarkupLine("[red]❌ DI Configuration Error:[/]");
            AnsiConsole.MarkupLine($"[yellow]{ex.Message}[/]");
            AnsiConsole.MarkupLine("\n[cyan]This is likely a coding error. Please check DependencyInjection.cs[/]");
            throw; // Re-throw vì đây là lỗi nghiêm trọng
        }
    }

    private static async Task RunInteractiveLoopAsync(TextToSqlAgentOrchestrator agent, IServiceProvider services)
    {
        var commandHandler = new CommandHandler(agent, services);
        var queryCount = 0;

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

            // Handle commands
            var commandResult = await commandHandler.HandleAsync(question);

            if (commandResult == CommandResult.Exit)
            {
                break;
            }

            if (commandResult == CommandResult.Handled)
            {
                continue;
            }

            // Process query
            queryCount++;
            await ProcessQueryAsync(agent, question, queryCount);

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
        }

        ConsoleUI.DisplayGoodbye();
    }

    private static async Task ProcessQueryAsync(
        TextToSqlAgentOrchestrator agent,
        string question,
        int queryNumber)
    {
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("green bold"))
                .StartAsync("[yellow]Processing your question...[/]", async ctx =>
                {
                    var response = await agent.ProcessQueryAsync(question);
                    ctx.Status("[green]Done![/]");
                    await Task.Delay(500);

                    AnsiConsole.WriteLine();
                    ResponseFormatter.Display(response, queryNumber);
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            ConsoleUI.DisplayError(ex);
        }
    }
    /// <summary>
    /// Set up Vietnamese language support in the console.
    /// </summary>
    private static void ConfigureVietnameseSupport()
    {
        try
        {
            // Thiết lập UTF-8 encoding cho Console
            System.Console.OutputEncoding = Encoding.UTF8;
            System.Console.InputEncoding = Encoding.UTF8;

            // Thiết lập culture cho tiếng Việt
            var vietnameseCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = vietnameseCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = vietnameseCulture;

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

            Log.Debug("Vietnamese support configured successfully");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not fully configure Vietnamese support, but continuing...");
        }
    }

    private static async Task<bool> TestDatabaseConnectionAsync(SqlExecutor executor)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[yellow]Testing database connection...[/]", async ctx =>
            {
                return await executor.ValidateConnectionAsync();
            });
    }
}