using Spectre.Console;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Console.UI;

public static class TableRenderer
{
    public static void Display(SqlExecutionResult result)
    {
        if (result.RowCount == 0)
        {
            AnsiConsole.MarkupLine("[dim]No results returned[/]");
            return;
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Grey));

        // Add columns
        foreach (var column in result.Columns)
        {
            table.AddColumn(new TableColumn($"[bold yellow]{column}[/]").Centered());
        }

        // Add rows (limit to 20)
        var rowsToDisplay = Math.Min(result.RowCount, 20);

        for (int i = 0; i < rowsToDisplay; i++)
        {
            var row = result.Rows[i];
            var cells = result.Columns.Select(col => FormatCellValue(row, col)).ToArray();
            table.AddRow(cells);
        }

        AnsiConsole.Write(table);

        if (result.RowCount > 20)
        {
            AnsiConsole.MarkupLine($"[dim]... and {result.RowCount - 20} more rows[/]");
        }

        AnsiConsole.MarkupLine($"[dim]Total: {result.RowCount} rows[/]");
    }

    private static string FormatCellValue(Dictionary<string, object> row, string columnName)
    {
        if (!row.ContainsKey(columnName))
        {
            return "[dim italic]NULL[/]";
        }

        var value = row[columnName];

        if (value == null || value == DBNull.Value)
        {
            return "[dim italic]NULL[/]";
        }

        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            decimal or double or float => string.Format("{0:N2}", value),
            _ => value.ToString() ?? ""
        };
    }
}