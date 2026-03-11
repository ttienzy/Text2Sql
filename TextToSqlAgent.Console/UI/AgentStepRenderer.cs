using Spectre.Console;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Console.UI;

/// <summary>
/// Renders agent reasoning steps and thinking process
/// </summary>
public static class AgentStepRenderer
{
    /// <summary>
    /// Display agent processing steps with visual indicators - MINIMAL OUTPUT
    /// </summary>
    public static void DisplayProcessingSteps(List<string> steps)
    {
        if (steps == null || steps.Count == 0)
        {
            return;
        }

        // Only show key milestones, not every step
        var keyMilestones = new[] { "validate", "schema", "rag", "generate", "execute" };
        var filteredSteps = steps.Where(s =>
            keyMilestones.Any(m => s.ToLowerInvariant().Contains(m))).ToList();

        if (filteredSteps.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();

        // Show as a simple list without excessive formatting
        foreach (var step in filteredSteps)
        {
            var icon = GetStepIcon(step);
            AnsiConsole.MarkupLine($"[dim]{icon} {step}[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display query validation results - CONCISE OUTPUT
    /// </summary>
    public static void DisplayValidationResult(bool isRelevant, string? message, double confidence)
    {
        if (!isRelevant)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️ {message ?? "Query not relevant to database"}[/]");
        }
        else if (confidence < 0.7)
        {
            AnsiConsole.MarkupLine($"[dim]ℹ️  Confidence: {confidence:P0}[/]");
        }
    }

    /// <summary>
    /// Display self-correction attempts - ONLY WHEN NEEDED
    /// </summary>
    public static void DisplayCorrectionAttempts(List<CorrectionAttempt> corrections)
    {
        if (corrections == null || corrections.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]🔧 Self-Correction: {corrections.Count} attempt(s)[/]");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn("[dim]#[/]")
            .AddColumn("[dim]Error[/]")
            .AddColumn("[dim]Fix[/]");

        for (int i = 0; i < corrections.Count; i++)
        {
            var correction = corrections[i];
            var errorShort = TruncateText(correction.Error?.ErrorMessage ?? "Unknown error", 40);
            var fixShort = TruncateText(correction.Reasoning, 50);

            table.AddRow(
                $"[dim]{i + 1}[/]",
                $"[red]{errorShort}[/]",
                correction.Success ? $"[green]{fixShort}[/]" : "[red]Failed[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display conversation context indicator - MINIMAL
    /// </summary>
    public static void DisplayConversationContext(int turnCount, bool hasContext)
    {
        if (turnCount > 1 && hasContext)
        {
            AnsiConsole.MarkupLine($"[dim]💬 Turn #{turnCount} (context-aware)[/]");
        }
    }

    /// <summary>
    /// Display query explanation - ONLY WHEN ENABLED
    /// </summary>
    public static void DisplayQueryExplanation(string? explanation)
    {
        if (string.IsNullOrWhiteSpace(explanation))
        {
            return;
        }

        var panel = new Panel(
            new Markup($"[cyan]{explanation}[/]"))
        {
            Header = new PanelHeader("📖 Query Explanation", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display agent metrics summary - CONCISE
    /// </summary>
    public static void DisplayMetricsSummary(
        TimeSpan processingTime,
        int stepsCount,
        int correctionAttempts,
        bool success)
    {
        var statusIcon = success ? "[green]✓[/]" : "[red]✗[/]";
        var statusText = success ? "[green]Success[/]" : "[red]Failed[/]";

        AnsiConsole.MarkupLine(
            $"[dim]{statusIcon} {statusText} | ⏱️  {processingTime.TotalSeconds:F2}s | 🔧 {correctionAttempts} corrections[/]");
    }

    private static string GetStepIcon(string step)
    {
        return step.ToLowerInvariant() switch
        {
            var s when s.Contains("validate") => "✅",
            var s when s.Contains("normalize") => "📝",
            var s when s.Contains("schema") => "🗄️",
            var s when s.Contains("rag") || s.Contains("retrieve") => "🔍",
            var s when s.Contains("intent") || s.Contains("analyze") => "🧠",
            var s when s.Contains("generate") || s.Contains("sql") => "⚡",
            var s when s.Contains("execute") => "▶️",
            var s when s.Contains("explain") => "📖",
            var s when s.Contains("format") || s.Contains("answer") => "💬",
            _ => "•"
        };
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength - 3) + "...";
    }
}
