using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using TextToSqlAgent.Console.Agent;
using TextToSqlAgent.Console.UI;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace TextToSqlAgent.Console.Commands;

public class CommandHandler
{
    private readonly TextToSqlAgentOrchestrator _agent;
    private readonly IServiceProvider _serviceProvider;

    public CommandHandler(TextToSqlAgentOrchestrator agent, IServiceProvider serviceProvider)
    {
        _agent = agent;
        _serviceProvider = serviceProvider;
    }

    public async Task<CommandResult> HandleAsync(string input)
    {
        var command = input.Trim().ToLowerInvariant();

        return command switch
        {
            "help" or "?" or "trogiup" or "trợ giúp" => HandleHelp(),
            "clear" or "cls" or "xoa" or "xóa" => HandleClear(),
            "clear cache" or "refresh schema" or "lam moi" or "làm mới" or "xoa cache" or "xóa cache"
                => HandleClearCache(),
            "examples" or "vd" or "vi du" or "ví dụ" or "mau" or "mẫu"
                => HandleExamples(),

            "index" or "index schema" => await HandleIndexSchemaAsync(),
            "reindex" or "reindex schema" => await HandleReindexSchemaAsync(),
            "check index" => await HandleCheckIndexAsync(),

            // ✅ Debug commands
            "debug" or "debug qdrant" or "/debug-qdrant" => await HandleDebugQdrantAsync(),
            "recreate" or "recreate collection" => await HandleRecreateCollectionAsync(),

            "switch db" or "change db" or "connect" or "doi db" or "đổi db"
                => await HandleSwitchDatabaseAsync(),

            "show db" or "current db" or "connection info" or "hien thi db" or "hiển thị db"
                => HandleShowCurrentDatabase(),

            "exit" or "quit" or "bye" or "thoat" or "q" => CommandResult.Exit,
            _ => CommandResult.NotHandled
        };
    }

    private CommandResult HandleShowCurrentDatabase()
    {
        try
        {
            var dbConfig = _serviceProvider.GetRequiredService<Infrastructure.Configuration.DatabaseConfig>();

            AnsiConsole.WriteLine();
            var panel = new Panel(
                new Markup($"[cyan]{Configuration.ConnectionManager.MaskConnectionString(dbConfig.ConnectionString)}[/]"))
            {
                Header = new PanelHeader("📊 Current Database Connection", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Cyan)
            };

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();

            return CommandResult.Handled;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Error: {ex.Message}[/]");
            return CommandResult.Handled;
        }
    }

    private async Task<CommandResult> HandleSwitchDatabaseAsync()
    {
        try
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]🔄 Switching database connection...[/]");
            AnsiConsole.WriteLine();

            var (connectionString, connectionName) = UI.ConsoleUI.PromptDatabaseConnection();

            var dbConfig = _serviceProvider.GetRequiredService<Infrastructure.Configuration.DatabaseConfig>();
            var sqlExecutor = _serviceProvider.GetRequiredService<SqlExecutor>();

            dbConfig.ConnectionString = connectionString;

            var isValid = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Testing new connection...[/]", async ctx =>
                {
                    return await sqlExecutor.ValidateConnectionAsync();
                });

            if (!isValid)
            {
                AnsiConsole.MarkupLine("[red]❌ Failed to connect to new database.[/]");
                AnsiConsole.MarkupLine("[yellow]⚠️  Keeping previous connection.[/]");
                return CommandResult.Handled;
            }

            AnsiConsole.MarkupLine($"[green]✓ Successfully switched to:[/] [cyan]{connectionName}[/]");
            AnsiConsole.MarkupLine($"[dim]Info:[/] [grey]{Configuration.ConnectionManager.MaskConnectionString(connectionString)}[/]");

            _agent.ClearSchemaCache();
            AnsiConsole.MarkupLine("[yellow]⚠️  Schema cache cleared. Will rescan on next query.[/]");

            AnsiConsole.WriteLine();
            return CommandResult.Handled;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Error switching database: {ex.Message}[/]");
            return CommandResult.Handled;
        }
    }

    private async Task<CommandResult> HandleReindexSchemaAsync()
    {
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Đang reindex schema...[/]", async ctx =>
                {
                    var indexer = _serviceProvider.GetRequiredService<SchemaIndexer>();
                    var scanner = _serviceProvider.GetRequiredService<SchemaScanner>();

                    ctx.Status("[yellow]Bước 1/3: Xóa index cũ...[/]");
                    await indexer.ClearIndexAsync();

                    ctx.Status("[yellow]Bước 2/3: Quét schema database...[/]");
                    var schema = await scanner.ScanAsync();

                    ctx.Status("[yellow]Bước 3/3: Embedding và lưu vào Qdrant...[/]");
                    await indexer.IndexSchemaAsync(schema);
                });

            AnsiConsole.MarkupLine("[green]✓ Schema đã được reindex thành công![/]");
            AnsiConsole.WriteLine();

            return CommandResult.Handled;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Lỗi khi reindex: {ex.Message}[/]");
            return CommandResult.Handled;
        }
    }

    private async Task<CommandResult> HandleCheckIndexAsync()
    {
        try
        {
            var qdrant = _serviceProvider.GetRequiredService<QdrantService>();

            var exists = await qdrant.CollectionExistsAsync();

            if (!exists)
            {
                AnsiConsole.MarkupLine("[red]❌ Collection chưa được tạo. Chạy 'index' để tạo.[/]");
                return CommandResult.Handled;
            }

            var count = await qdrant.GetPointCountAsync();

            AnsiConsole.MarkupLine($"[green]✓ Collection exists[/]");
            AnsiConsole.MarkupLine($"[cyan]Points count: {count}[/]");

            if (count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]⚠️  Chưa có data. Chạy 'index' để index schema.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ Schema đã được index ({count} documents)[/]");
            }

            return CommandResult.Handled;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Lỗi: {ex.Message}[/]");
            return CommandResult.Handled;
        }
    }

    private async Task<CommandResult> HandleDebugQdrantAsync()
    {
        var qdrant = _serviceProvider.GetRequiredService<QdrantService>();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow bold]🔍 QDRANT DIAGNOSTICS[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        try
        {
            var diagnostics = new Table()
                .Border(TableBorder.Rounded)
                .BorderStyle(new Style(Color.Cyan))
                .AddColumn("[cyan bold]Check[/]")
                .AddColumn("[cyan bold]Status[/]")
                .AddColumn("[cyan bold]Details[/]");

            // 1. Collection Name
            var collectionName = qdrant.GetCurrentCollectionName();
            diagnostics.AddRow(
                "Collection Name",
                "[white]ℹ️[/]",
                $"[yellow]{collectionName}[/]");

            // 2. Connection Test
            string connectionStatus, connectionDetails;
            try
            {
                var exists = await qdrant.CollectionExistsAsync();
                connectionStatus = exists ? "[green]✓ OK[/]" : "[yellow]⚠️ Not Found[/]";
                connectionDetails = exists ? "Collection exists" : "Collection not found";
            }
            catch (Exception ex)
            {
                connectionStatus = "[red]✗ FAIL[/]";
                connectionDetails = $"Error: {ex.Message}";
            }

            diagnostics.AddRow("Connection", connectionStatus, connectionDetails);

            // 3. Collection Info
            string infoStatus, infoDetails;
            long pointCount = 0;
            int actualVectorSize = 0;

            try
            {
                var info = await qdrant.GetCollectionInfoAsync();
                if (info != null)
                {
                    pointCount = info.PointsCount;
                    actualVectorSize = info.Config?.Params?.Vectors?.Size ?? 0;

                    infoStatus = "[green]✓ OK[/]";
                    infoDetails = $"Points: {pointCount}, " +
                                 $"VectorSize: {actualVectorSize}, " +
                                 $"Distance: {info.Config?.Params?.Vectors?.Distance ?? "unknown"}";
                }
                else
                {
                    infoStatus = "[red]✗ FAIL[/]";
                    infoDetails = "Cannot retrieve info";
                }
            }
            catch (Exception ex)
            {
                infoStatus = "[red]✗ FAIL[/]";
                infoDetails = ex.Message;
            }

            diagnostics.AddRow("Collection Info", infoStatus, infoDetails);

            // 4. Vector Size Compatibility
            string vectorStatus, vectorDetails;
            try
            {
                var compatible = await qdrant.IsVectorSizeCompatibleAsync();
                var qdrantConfig = _serviceProvider.GetRequiredService<Infrastructure.Configuration.QdrantConfig>();
                var expectedSize = qdrantConfig.VectorSize;

                vectorStatus = compatible ? "[green]✓ MATCH[/]" : "[red]✗ MISMATCH[/]";
                vectorDetails = $"Expected: {expectedSize}, Actual: {actualVectorSize}";

                if (!compatible && actualVectorSize > 0)
                {
                    vectorDetails += " [red bold](⚠️ CRITICAL!)[/]";
                }
            }
            catch (Exception ex)
            {
                vectorStatus = "[red]✗ ERROR[/]";
                vectorDetails = ex.Message;
            }

            diagnostics.AddRow("Vector Size", vectorStatus, vectorDetails);

            // 5. Point Count
            string countStatus = pointCount > 0 ? "[green]✓ OK[/]" : "[yellow]⚠️ EMPTY[/]";
            string countDetails = $"{pointCount} points";

            if (pointCount == 0)
            {
                countDetails += " [red](Collection trống!)[/]";
            }

            diagnostics.AddRow("Point Count", countStatus, countDetails);

            AnsiConsole.Write(diagnostics);
            AnsiConsole.WriteLine();

            // Recommendations
            if (pointCount == 0)
            {
                var panel = new Panel(
                    new Markup(
                        "[yellow]💡 Gợi ý:[/]\n" +
                        "  • Chạy [cyan]'index'[/] để index database schema\n" +
                        "  • Hoặc [cyan]'recreate'[/] nếu có vấn đề về cấu hình"))
                {
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Yellow)
                };
                AnsiConsole.Write(panel);
            }
            else
            {
                AnsiConsole.MarkupLine("[green]✓ Qdrant hoạt động bình thường![/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Lỗi khi chẩn đoán: {ex.Message}[/]");
        }

        AnsiConsole.WriteLine();
        return CommandResult.Handled;
    }

    private async Task<CommandResult> HandleRecreateCollectionAsync()
    {
        var qdrant = _serviceProvider.GetRequiredService<QdrantService>();

        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm(
            "[yellow]⚠️  Thao tác này sẽ XÓA collection hiện tại và tất cả dữ liệu. Tiếp tục?[/]",
            false);

        if (!confirm)
        {
            AnsiConsole.MarkupLine("[cyan]Đã hủy thao tác.[/]");
            return CommandResult.Handled;
        }

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Đang recreate collection...[/]", async ctx =>
                {
                    ctx.Status("[yellow]Bước 1/3: Xóa collection cũ...[/]");
                    await qdrant.DeleteCollectionAsync();
                    await Task.Delay(500);

                    ctx.Status("[yellow]Bước 2/3: Tạo collection mới...[/]");
                    await qdrant.CreateCollectionAsync();
                    await Task.Delay(500);

                    ctx.Status("[yellow]Bước 3/3: Xác minh...[/]");
                    var info = await qdrant.GetCollectionInfoAsync();

                    if (info == null)
                    {
                        throw new Exception("Không thể xác minh collection sau khi tạo");
                    }
                });

            AnsiConsole.MarkupLine("[green]✓ Collection đã được tạo lại thành công![/]");
            AnsiConsole.MarkupLine("[yellow]💡 Chạy [cyan]'index'[/] để index lại database schema[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Lỗi khi recreate collection: {ex.Message}[/]");
        }

        AnsiConsole.WriteLine();
        return CommandResult.Handled;
    }

    private CommandResult HandleHelp()
    {
        ConsoleUI.DisplayHelp();
        return CommandResult.Handled;
    }

    private CommandResult HandleClear()
    {
        AnsiConsole.Clear();
        
        try
        {
            var configuration = _serviceProvider.GetRequiredService<IConfiguration>();
            var providerString = configuration["LLMProvider"] ?? "Gemini";
            var provider = Enum.Parse<LLMProvider>(providerString, ignoreCase: true);
            
            string modelName;
            if (provider == LLMProvider.OpenAI)
            {
                var openAIConfig = _serviceProvider.GetRequiredService<OpenAIConfig>();
                modelName = openAIConfig.Model;
            }
            else
            {
                var geminiConfig = _serviceProvider.GetRequiredService<GeminiConfig>();
                modelName = geminiConfig.Model;
            }

            ConsoleUI.DisplayWelcomeBanner(provider, modelName);
        }
        catch
        {
            // Fallback if something goes wrong
            ConsoleUI.DisplayWelcomeBanner(LLMProvider.Gemini, "Unknown");
        }
        
        return CommandResult.Handled;
    }

    private CommandResult HandleClearCache()
    {
        _agent.ClearSchemaCache();
        AnsiConsole.MarkupLine("[yellow]✓ Schema cache đã xóa. Sẽ quét lại ở query tiếp theo.[/]");
        return CommandResult.Handled;
    }

    private CommandResult HandleExamples()
    {
        ConsoleUI.DisplayExamples();
        return CommandResult.Handled;
    }

    private async Task<CommandResult> HandleIndexSchemaAsync()
    {
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Đang index schema vào vector database...[/]", async ctx =>
                {
                    var indexer = _serviceProvider.GetRequiredService<SchemaIndexer>();
                    var scanner = _serviceProvider.GetRequiredService<SchemaScanner>();

                    ctx.Status("[yellow]Bước 1/2: Quét schema database...[/]");
                    var schema = await scanner.ScanAsync();

                    ctx.Status("[yellow]Bước 2/2: Embedding và lưu vào Qdrant...[/]");
                    await indexer.IndexSchemaAsync(schema);
                });

            AnsiConsole.MarkupLine("[green]✓ Schema đã được index thành công vào vector database![/]");
            AnsiConsole.WriteLine();

            return CommandResult.Handled;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Lỗi khi index schema: {ex.Message}[/]");
            AnsiConsole.WriteException(ex);
            return CommandResult.Handled;
        }
    }
}
