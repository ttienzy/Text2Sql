using Spectre.Console;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Console.UI;

public static class ConsoleUI
{
    public static void DisplayWelcomeBanner()
    {
        AnsiConsole.Clear();

        var rule = new Rule("[blue bold]TEXT TO SQL AGENT[/]");
        rule.Justification = Justify.Center;
        AnsiConsole.Write(rule);

        AnsiConsole.WriteLine();

        var grid = new Grid();
        grid.AddColumn();
        grid.AddRow("[dim]Powered by:[/] [cyan]Gemini 2.0 Flash[/]");
        grid.AddRow("[dim]Version:[/] [green]1.0.0 (Week 1-2 MVP)[/]");
        grid.AddRow("[dim]Author:[/] [yellow]Text To SQL Team[/]");

        AnsiConsole.Write(Align.Center(grid));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public static void DisplayConfigurationInfo(GeminiConfig geminiConfig, AgentConfig agentConfig)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Grey));
        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("LLM Model", $"[cyan]{geminiConfig.Model}[/]");
        table.AddRow("Temperature", $"[cyan]{geminiConfig.Temperature}[/]");
        table.AddRow("Max Tokens", $"[cyan]{geminiConfig.MaxTokens}[/]");
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

    public static (string connectionString, string connectionName) PromptDatabaseConnection()
    {
        AnsiConsole.MarkupLine("[yellow]📊 Database Connection Setup[/]");
        AnsiConsole.WriteLine();

        var connectionManager = new Configuration.ConnectionManager();
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

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Choose how to connect:[/]")
                .PageSize(15)
                .AddChoices(choices.Where(c => !string.IsNullOrEmpty(c))));

        string connectionString;
        string connectionName;

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

            // Update last used
            savedConnection.LastUsed = DateTime.Now;
            data.LastUsedConnectionName = selectedName;
            connectionManager.SaveConnections(data);

            AnsiConsole.MarkupLine($"[green]✓ Loaded saved connection[/]");
        }
        else
        {
            // Use interactive builder
            AnsiConsole.WriteLine();
            var (builtConnection, serverName, databaseName) = ConnectionBuilder.BuildConnectionString();

            if (string.IsNullOrEmpty(builtConnection))
            {
                // User cancelled, restart the flow
                AnsiConsole.MarkupLine("[yellow]← Returning to connection menu...[/]");
                AnsiConsole.WriteLine();
                return PromptDatabaseConnection();
            }

            connectionString = builtConnection;

            var saveOption = AnsiConsole.Confirm(
                "[yellow]💾 Save this connection for future use?[/]",
                defaultValue: true);

            if (saveOption)
            {
                connectionName = AnsiConsole.Prompt(
                    new TextPrompt<string>("[yellow]Enter a name for this connection:[/]")
                        .PromptStyle("green")
                        .DefaultValue($"Connection {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .ValidationErrorMessage("[red]Name cannot be empty[/]")
                        .Validate(s => !string.IsNullOrWhiteSpace(s)));

                connectionManager.AddOrUpdateConnection(data, connectionName, connectionString);
                AnsiConsole.MarkupLine("[green]✓ Connection saved![/]");
            }
            else
            {
                connectionName = $"Temp Connection {DateTime.Now:HH:mm:ss}";
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Selected:[/] [cyan]{connectionName}[/]");
        AnsiConsole.MarkupLine($"[dim]Info:[/] [grey]{Configuration.ConnectionManager.MaskConnectionString(connectionString)}[/]");
        AnsiConsole.WriteLine();

        return (connectionString, connectionName);
    }

    public static void DisplayHelp()
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Blue));
        table.AddColumn(new TableColumn("[bold yellow]Lệnh[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold yellow]Mô tả[/]").LeftAligned());

        // ═══ BASIC COMMANDS ═══
        table.AddRow("[green bold]═══ CƠ BẢN ═══[/]", "");
        table.AddRow("[cyan]help[/], [cyan]?[/]", "Hiển thị trợ giúp");
        table.AddRow("[cyan]examples[/]", "Hiển thị câu hỏi mẫu");
        table.AddRow("[cyan]clear[/], [cyan]cls[/]", "Xóa màn hình");

        table.AddEmptyRow();

        // ═══ SCHEMA & INDEX ═══
        table.AddRow("[green bold]═══ SCHEMA & INDEX ═══[/]", "");
        table.AddRow("[cyan]index[/]", "Index database schema vào vector DB");
        table.AddRow("[cyan]reindex[/]", "Xóa và index lại toàn bộ schema");
        table.AddRow("[cyan]check index[/]", "Kiểm tra trạng thái index hiện tại");
        table.AddRow("[cyan]clear cache[/]", "Xóa schema cache và làm mới");

        table.AddEmptyRow();

        // ═══ DEBUG & TROUBLESHOOTING ═══
        table.AddRow("[yellow bold]═══ DEBUG & SỬA LỖI ═══[/]", "");
        table.AddRow("[cyan]debug[/]", "[green]🔧[/] Chẩn đoán Qdrant (kết nối, cấu hình, dữ liệu)");
        table.AddRow("[cyan]recreate[/]", "[red]⚠️[/] Xóa và tạo lại Qdrant collection");

        table.AddEmptyRow();

        // ═══ DATABASE ═══
        table.AddRow("[blue bold]═══ DATABASE ═══[/]", "");
        table.AddRow("[cyan]show db[/]", "Hiển thị kết nối database hiện tại");
        table.AddRow("[cyan]switch db[/]", "Chuyển sang database khác");

        table.AddEmptyRow();

        // ═══ EXIT ═══
        table.AddRow("[cyan]exit[/], [cyan]quit[/]", "Thoát chương trình");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("📚 CÁC LỆNH KHẢ DỤNG", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // ═══ TIPS ═══
        var tipsPanel = new Panel(
            new Markup(
                "[dim]💡 Mẹo:[/]\n" +
                "  • Nếu câu trả lời không chính xác, thử [cyan]'reindex'[/] để làm mới schema\n" +
                "  • Khi gặp lỗi kết nối Qdrant, chạy [cyan]'debug'[/] để chẩn đoán\n" +
                "  • Vector size mismatch? Chạy [cyan]'recreate'[/] rồi [cyan]'index'[/]"))
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
        table.AddColumn("[bold]Danh mục[/]");
        table.AddColumn("[bold]Câu hỏi mẫu[/]");

        table.AddRow("[yellow]Schema[/]", "Có bao nhiêu bảng trong database?");
        table.AddRow("[yellow]Count[/]", "Có bao nhiêu khách hàng?");
        table.AddRow("[yellow]List[/]", "Liệt kê tất cả khách hàng");
        table.AddRow("[yellow]Filter[/]", "Khách hàng nào ở Hà Nội?");
        table.AddRow("[yellow]Aggregate[/]", "Top 5 sản phẩm bán chạy nhất");
        table.AddRow("[yellow]Date Range[/]", "Đơn hàng tuần này");
        table.AddRow("[yellow]Join[/]", "Đơn hàng của khách hàng Nguyễn Văn A");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("💡 Câu hỏi mẫu", Justify.Left),
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
            Header = new PanelHeader("❌ Lỗi không mong đợi", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red)
        };

        AnsiConsole.Write(panel);

        if (ex.InnerException != null)
        {
            AnsiConsole.MarkupLine($"[dim]Chi tiết: {ex.InnerException.Message}[/]");
        }

        AnsiConsole.WriteLine();
    }

    public static void DisplayGoodbye()
    {
        AnsiConsole.WriteLine();

        var panel = new Panel(
            Align.Center(
                new Markup("[green bold]Cảm ơn bạn đã sử dụng Text To SQL Agent!\n\n[dim]Chúc bạn làm việc hiệu quả! 🚀[/]"),
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