namespace TextToSqlAgent.Application.Adapters;

using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;

public class IntelligentResultFormatter : IResultFormatter
{
    public string FormatAnswer(
        IntentAnalysisResult intent,
        SqlExecutionResult result,
        ConversationContext? context = null)
    {
        var answer = "";

        // Add context-aware response
        if (context != null && context.TurnCount > 1)
        {
            answer += "📊 ";
        }

        if (result.RowCount == 0)
        {
            return answer + "No results found. Try adjusting your filters or criteria.";
        }

        answer += intent.Intent switch
        {
            QueryIntent.COUNT => $"Found {result.Rows[0].Values.First()} records.",
            QueryIntent.LIST => $"Retrieved {result.RowCount} record(s).",
            QueryIntent.SCHEMA when intent.Target.Equals("TABLES", StringComparison.OrdinalIgnoreCase) =>
                $"Database contains {result.RowCount} table(s).",
            QueryIntent.AGGREGATE => $"Analysis complete: {result.RowCount} group(s) found.",
            QueryIntent.DETAIL => $"Retrieved detailed information: {result.RowCount} record(s).",
            _ => $"Query successful: {result.RowCount} result(s)."
        };

        return answer;
    }
}
