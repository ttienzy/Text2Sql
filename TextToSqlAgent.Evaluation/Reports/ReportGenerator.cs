using System.Text;
using Newtonsoft.Json;
using TextToSqlAgent.Evaluation.Models;

namespace TextToSqlAgent.Evaluation.Reports;

/// <summary>
/// Generate evaluation reports in various formats
/// P1-08: Enhanced with result validation metrics
/// </summary>
public class ReportGenerator
{
    public string GenerateConsoleReport(EvaluationReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("╔════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║           TEXT-TO-SQL EVALUATION REPORT                        ║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"Version: {report.Version}");
        sb.AppendLine($"Timestamp: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total Examples: {report.TotalExamples}");
        sb.AppendLine();

        sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ ACCURACY METRICS (P1-08 Enhanced)                          │");
        sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
        sb.AppendLine($"│ Execution Accuracy:    {report.ExecutionAccuracy,6:F2}% (queries ran)         │");
        sb.AppendLine($"│ Exact Match Accuracy:  {report.ExactMatchAccuracy,6:F2}% (SQL matches)        │");
        sb.AppendLine($"│ Result Accuracy:       {report.ResultAccuracy,6:F2}% (TRUE correctness) │");
        sb.AppendLine($"│ Avg Result Similarity: {report.AvgResultSimilarity,6:F2}% (partial credit)    │");
        sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
        sb.AppendLine();

        // P1-08: Show incorrect results analysis
        if (report.IncorrectResults.Any())
        {
            sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│ INCORRECT RESULTS ANALYSIS (P1-08)                          │");
            sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
            sb.AppendLine($"│ Executed but Wrong:    {report.IncorrectResults.Count,3} examples                      │");
            sb.AppendLine($"│ (Success=true but ResultMatch=false)                        │");
            sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
            sb.AppendLine();
        }

        sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ SCHEMA LINKING METRICS                                      │");
        sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
        sb.AppendLine($"│ Precision:             {report.AvgSchemaLinkingPrecision,6:F2}%                      │");
        sb.AppendLine($"│ Recall:                {report.AvgSchemaLinkingRecall,6:F2}%                      │");
        sb.AppendLine($"│ F1 Score:              {report.SchemaLinkingF1,6:F2}%                      │");
        sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
        sb.AppendLine();

        sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│ PERFORMANCE METRICS                                         │");
        sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
        sb.AppendLine($"│ Avg Latency:           {report.AvgLatencyMs,6:F0} ms                       │");
        sb.AppendLine($"│ P50 Latency:           {report.P50LatencyMs,6:F0} ms                       │");
        sb.AppendLine($"│ P95 Latency:           {report.P95LatencyMs,6:F0} ms                       │");
        sb.AppendLine($"│ P99 Latency:           {report.P99LatencyMs,6:F0} ms                       │");
        sb.AppendLine($"│ Total Tokens:          {report.TotalTokensUsed,6:N0}                          │");
        sb.AppendLine($"│ Avg Tokens/Query:      {report.AvgTokensPerQuery,6:F0}                          │");
        sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
        sb.AppendLine();

        if (report.AccuracyByDifficulty.Any())
        {
            sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│ EXECUTION ACCURACY BY DIFFICULTY                            │");
            sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
            foreach (var kvp in report.AccuracyByDifficulty.OrderBy(x => x.Key))
            {
                sb.AppendLine($"│ {kvp.Key,-15}        {kvp.Value,6:F2}%                      │");
            }
            sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
            sb.AppendLine();
        }

        // P1-08: Result accuracy by difficulty
        if (report.ResultAccuracyByDifficulty.Any())
        {
            sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│ RESULT ACCURACY BY DIFFICULTY (P1-08)                       │");
            sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
            foreach (var kvp in report.ResultAccuracyByDifficulty.OrderBy(x => x.Key))
            {
                sb.AppendLine($"│ {kvp.Key,-15}        {kvp.Value,6:F2}%                      │");
            }
            sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
            sb.AppendLine();
        }

        if (report.ErrorTypes.Any())
        {
            sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
            sb.AppendLine("│ ERROR ANALYSIS                                              │");
            sb.AppendLine("├─────────────────────────────────────────────────────────────┤");
            foreach (var kvp in report.ErrorTypes.OrderByDescending(x => x.Value).Take(5))
            {
                sb.AppendLine($"│ {kvp.Key,-40} {kvp.Value,5} │");
            }
            sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
            sb.AppendLine();
        }

        sb.AppendLine($"Failed Examples: {report.FailedExamples.Count}/{report.TotalExamples}");
        sb.AppendLine($"Incorrect Results: {report.IncorrectResults.Count}/{report.TotalExamples} (P1-08)");

        return sb.ToString();
    }

    public async Task SaveJsonReportAsync(EvaluationReport report, string filePath)
    {
        var json = JsonConvert.SerializeObject(report, Formatting.Indented);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task SaveCsvReportAsync(EvaluationReport report, string filePath)
    {
        var sb = new StringBuilder();
        // P1-08: Added ResultMatch and ResultSimilarity columns
        sb.AppendLine("Id,Question,Difficulty,ExecutionSuccess,ExactMatch,ResultMatch,ResultSimilarity,LatencyMs,TokensUsed,ErrorMessage,ValidationFailureReason");

        foreach (var result in report.Results)
        {
            sb.AppendLine($"\"{result.Example.Id}\"," +
                         $"\"{result.Example.Question}\"," +
                         $"\"{result.Example.Difficulty}\"," +
                         $"{result.ExecutionSuccess}," +
                         $"{result.ExactMatch}," +
                         $"{result.ResultMatch}," +
                         $"{result.ResultSimilarity:F2}," +
                         $"{result.LatencyMs}," +
                         $"{result.TokensUsed}," +
                         $"\"{result.ErrorMessage?.Replace("\"", "\"\"")}\"," +
                         $"\"{result.ValidationFailureReason?.Replace("\"", "\"\"")}\"");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }
}