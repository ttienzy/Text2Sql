using Spectre.Console;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Console.UI;

public static class ResponseFormatter
{
    public static void Display(AgentResponse response, int queryNumber)
    {
        if (response.Success)
        {
            DisplaySuccess(response, queryNumber);
        }
        else
        {
            DisplayError(response);
        }
    }

    private static void DisplaySuccess(AgentResponse response, int queryNumber)
    {
        if (response.WasCorrected)
        {
            DisplayCorrectionInfo(response.CorrectionHistory);
        }
        // Answer panel
        var answerPanel = new Panel(new Markup($"[green]{response.Answer}[/]"))
        {
            Header = new PanelHeader($"🤖 Answer (Query #{queryNumber})", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(1, 1, 1, 1)
        };
        AnsiConsole.Write(answerPanel);
        AnsiConsole.WriteLine();

        // SQL panel
        if (!string.IsNullOrEmpty(response.SqlGenerated))
        {
            var sqlPanel = new Panel(response.SqlGenerated)
            {
                Header = new PanelHeader("📝 Generated SQL", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.Write(sqlPanel);
            AnsiConsole.WriteLine();
        }

        // Result table
        if (response.QueryResult?.Success == true && response.QueryResult.RowCount > 0)
        {
            TableRenderer.Display(response.QueryResult);
            AnsiConsole.WriteLine();
        }

        // Processing steps
        if (response.ProcessingSteps.Any())
        {
            DisplayProcessingSteps(response.ProcessingSteps);
        }
    }

    private static void DisplayError(AgentResponse response)
    {
        var errorMessage = response.ErrorMessage ?? "Unknown error occurred";

        var errorPanel = new Panel(new Markup($"[red]{Markup.Escape(errorMessage)}[/]"))  // ← Escape
        {
            Header = new PanelHeader("❌ Error", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red),
            Padding = new Padding(1, 1, 1, 1)
        };
        AnsiConsole.Write(errorPanel);

        AnsiConsole.WriteLine();

        // Show attempted SQL if available
        if (!string.IsNullOrEmpty(response.SqlGenerated))
        {
            AnsiConsole.MarkupLine("[dim]SQL attempted:[/]");
            var sqlPanel = new Panel(Markup.Escape(response.SqlGenerated))  // ← Escape
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey)
            };
            AnsiConsole.Write(sqlPanel);
            AnsiConsole.WriteLine();
        }

        // Suggestions
        AnsiConsole.MarkupLine("[yellow]💡 Suggestions:[/]");
        AnsiConsole.MarkupLine("  • Try rephrasing the question");
        AnsiConsole.MarkupLine("  • Check table/column names");
        AnsiConsole.MarkupLine("  • Type 'examples' for samples");

        // If was corrected
        if (response.WasCorrected)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]⚠️  Attempted self-correction {response.CorrectionAttempts} times but failed.[/]");
        }
    }
    private static void DisplayCorrectionInfo(List<CorrectionAttempt> corrections)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Yellow));
        table.AddColumn("[bold]Attempt[/]");
        table.AddColumn("[bold]Error[/]");
        table.AddColumn("[bold]Fix[/]");

        foreach (var correction in corrections)
        {
            table.AddRow(
                $"#{correction.AttemptNumber}",
                $"[red]{correction.Error.Type}[/]\n[dim]{correction.Error.InvalidElement}[/]",
                correction.Success ? "[green]✓ Fixed[/]" : "[red]✗ Failed[/]"
            );
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader("🔧 Self-Correction History", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static void DisplayProcessingSteps(List<string> steps)
    {
        var tree = new Tree("[dim]Processing Steps:[/]");
        foreach (var step in steps)
        {
            tree.AddNode($"[dim]{step}[/]");
        }
        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();
    }
}