using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Analysis;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Plugins;

public class SqlCorrectorPlugin
{
    private readonly ILLMClient _llmClient;
    private readonly SqlErrorAnalyzer _errorAnalyzer;
    private readonly IDatabaseAdapter _adapter;
    private readonly PromptRegistry _promptRegistry;
    private readonly ILogger<SqlCorrectorPlugin> _logger;

    public SqlCorrectorPlugin(
        ILLMClient llmClient,
        SqlErrorAnalyzer errorAnalyzer,
        IDatabaseAdapter adapter,
        PromptRegistry promptRegistry,
        ILogger<SqlCorrectorPlugin> logger)
    {
        _llmClient = llmClient;
        _errorAnalyzer = errorAnalyzer;
        _adapter = adapter;
        _promptRegistry = promptRegistry;
        _logger = logger;
    }

    [KernelFunction, Description("Automatically correct SQL errors based on error message and schema")]
    public async Task<CorrectionAttempt> CorrectSqlAsync(
        string originalSql,
        string errorMessage,
        RetrievedSchemaContext schemaContext,
        IntentAnalysis intent,
        int attemptNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SQL Corrector] Attempting correction (Attempt {Attempt})", attemptNumber);

        var attempt = new CorrectionAttempt
        {
            AttemptNumber = attemptNumber,
            OriginalSql = originalSql,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var error = _errorAnalyzer.AnalyzeError(errorMessage, originalSql);
            attempt.Error = error;

            _logger.LogInformation(
                "[SQL Corrector] Error type: {Type}, Recoverable: {Recoverable}",
                error.Type,
                error.IsRecoverable);

            if (!error.IsRecoverable)
            {
                _logger.LogWarning("[SQL Corrector] Error is not auto-correctable");
                attempt.Success = false;
                attempt.Reasoning = "Error cannot be automatically corrected.";
                return attempt;
            }

            var schemaContextText = BuildSchemaContextForCorrection(schemaContext, error);
            var filterValue = intent.Filters.FirstOrDefault()?.Value;

            var promptVariables = new Dictionary<string, object>
            {
                ["provider"] = _adapter.Provider.ToString(),
                ["original_sql"] = originalSql,
                ["error_message"] = errorMessage,
                ["error_type"] = error.Type.ToString(),
                ["invalid_element"] = error.InvalidElement ?? string.Empty,
                ["schema_context"] = schemaContextText,
                ["filter_value"] = filterValue ?? string.Empty
            };

            var (systemPrompt, userPrompt) = _promptRegistry.GetSystemAndUserPrompts(
                "sql-generation/correction",
                new List<string>(),
                promptVariables,
                includeExamples: false);

            var correctedSql = await _llmClient.CompleteWithSystemPromptAsync(
                systemPrompt,
                userPrompt,
                cancellationToken);

            correctedSql = CleanSqlResponse(correctedSql);

            attempt.CorrectedSql = correctedSql;
            attempt.Success = true;
            attempt.Reasoning = BuildReasoningMessage(error, originalSql, correctedSql);

            _logger.LogInformation("[SQL Corrector] SQL corrected check");
            _logger.LogDebug("[SQL Corrector] Corrected SQL: {SQL}", correctedSql);

            return attempt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQL Corrector] Error correcting SQL");

            attempt.Success = false;
            attempt.Reasoning = $"Correction failed: {ex.Message}";
            return attempt;
        }
    }

    private string BuildSchemaContextForCorrection(RetrievedSchemaContext context, SqlError error)
    {
        var schemaText = string.Empty;

        foreach (var table in context.RelevantTables)
        {
            schemaText += "\n========================================\n";
            var safeSchema = _adapter.GetSafeIdentifier(table.Schema);
            var safeTable = _adapter.GetSafeIdentifier(table.TableName);

            schemaText += $"Table: {safeSchema}.{safeTable}\n";
            schemaText += "========================================\n";
            schemaText += "Columns (USE THESE EXACT NAMES):\n";

            foreach (var col in table.Columns)
            {
                var pk = col.IsPrimaryKey ? " <- PRIMARY KEY" : string.Empty;
                var fk = col.IsForeignKey ? " <- FOREIGN KEY" : string.Empty;
                var nullable = col.IsNullable ? " NULL" : " NOT NULL";
                var safeCol = _adapter.GetSafeIdentifier(col.ColumnName);

                schemaText += $"  {safeCol} ({col.DataType}{nullable}){pk}{fk}\n";
            }

            schemaText += "\n";
        }

        if (context.RelevantRelationships.Any())
        {
            schemaText += "========================================\n";
            schemaText += "RELATIONSHIPS (JOIN using these):\n";
            schemaText += "========================================\n";

            foreach (var rel in context.RelevantRelationships)
            {
                schemaText += $"  {rel.FromTable}.{rel.FromColumn} -> {rel.ToTable}.{rel.ToColumn}\n";
            }

            schemaText += "\n";
        }

        if (error.Type == SqlErrorType.InvalidColumnName && !string.IsNullOrEmpty(error.InvalidElement))
        {
            schemaText += "========================================\n";
            schemaText += "ERROR DETECTED:\n";
            schemaText += "========================================\n";
            schemaText += $"Column '{error.InvalidElement}' DOES NOT EXIST!\n";
            schemaText += "You MUST use one of the EXACT column names listed above.\n";
            schemaText += "Check the column list carefully and use the EXACT spelling.\n\n";

            var suggestions = FindSimilarColumns(error.InvalidElement, context);
            if (suggestions.Any())
            {
                schemaText += "Did you mean one of these?\n";
                foreach (var (table, column) in suggestions)
                {
                    var safeTable = _adapter.GetSafeIdentifier(table);
                    var safeColumn = _adapter.GetSafeIdentifier(column);
                    schemaText += $"  - {safeTable}.{safeColumn}\n";
                }
            }
        }

        return schemaText;
    }

    private List<(string Table, string Column)> FindSimilarColumns(
        string invalidColumn,
        RetrievedSchemaContext context)
    {
        var suggestions = new List<(string, string)>();
        var lowerInvalid = invalidColumn.ToLowerInvariant().Replace("_", string.Empty);

        foreach (var table in context.RelevantTables)
        {
            foreach (var col in table.Columns)
            {
                var lowerCol = col.ColumnName.ToLowerInvariant().Replace("_", string.Empty);
                if (lowerCol.Contains(lowerInvalid) || lowerInvalid.Contains(lowerCol))
                {
                    suggestions.Add((table.TableName, col.ColumnName));
                }
            }
        }

        return suggestions.Take(3).ToList();
    }

    private string BuildReasoningMessage(SqlError error, string originalSql, string correctedSql)
    {
        var message = $"Error: {error.Type}\n";

        if (!string.IsNullOrEmpty(error.InvalidElement))
        {
            message += $"Invalid element: '{error.InvalidElement}'\n";
        }

        message += $"Fix: {error.SuggestedFix}\n";

        if (!string.Equals(originalSql, correctedSql, StringComparison.Ordinal))
        {
            var changes = FindChanges(originalSql, correctedSql);
            if (changes.Any())
            {
                message += "\nChanges made:\n";
                foreach (var change in changes)
                {
                    message += $"  - {change}\n";
                }
            }
        }

        return message;
    }

    private List<string> FindChanges(string original, string corrected)
    {
        var changes = new List<string>();

        var originalWords = original.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var correctedWords = corrected.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Min(originalWords.Length, correctedWords.Length); i++)
        {
            if (!string.Equals(originalWords[i], correctedWords[i], StringComparison.Ordinal))
            {
                changes.Add($"'{originalWords[i]}' -> '{correctedWords[i]}'");
            }
        }

        return changes.Take(5).ToList();
    }

    private string CleanSqlResponse(string sql)
    {
        sql = Regex.Replace(sql, @"```sql\s*", string.Empty, RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"```\s*", string.Empty, RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"^SQL:\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"^Corrected SQL:\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Multiline);

        sql = sql.Trim();
        sql = sql.TrimEnd(';');

        return sql;
    }

    public bool ShouldRetry(List<CorrectionAttempt> attempts, int maxAttempts)
    {
        if (attempts.Count >= maxAttempts)
        {
            _logger.LogWarning("[SQL Corrector] Max attempts reached ({Max})", maxAttempts);
            return false;
        }

        var lastAttempt = attempts.LastOrDefault();
        if (lastAttempt == null)
        {
            return true;
        }

        if (!lastAttempt.Error.IsRecoverable)
        {
            _logger.LogWarning("[SQL Corrector] Error not recoverable, stopping retry");
            return false;
        }

        if (attempts.Count >= 2)
        {
            var lastTwo = attempts.TakeLast(2).ToList();
            if (lastTwo[0].CorrectedSql == lastTwo[1].CorrectedSql)
            {
                _logger.LogWarning("[SQL Corrector] SQL repeated, stopping retry");
                return false;
            }
        }

        return true;
    }
}
