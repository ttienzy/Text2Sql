using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
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
    private readonly ILogger<SqlCorrectorPlugin> _logger;

    public SqlCorrectorPlugin(
        ILLMClient llmClient,
        SqlErrorAnalyzer errorAnalyzer,
        IDatabaseAdapter adapter,
        ILogger<SqlCorrectorPlugin> logger)
    {
        _llmClient = llmClient;
        _errorAnalyzer = errorAnalyzer;
        _adapter = adapter;
        _logger = logger;
    }

    [KernelFunction, Description("Automatically correct SQL errors based on error message and schema")]
    public async Task<CorrectionAttempt> CorrectSqlAsync(
    string originalSql,
    string errorMessage,
    RetrievedSchemaContext schemaContext,
    IntentAnalysis intent,  // ← ADD to get filter values
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
            // 1. Analyze error
            var error = _errorAnalyzer.AnalyzeError(errorMessage, originalSql);
            attempt.Error = error;

            _logger.LogInformation("[SQL Corrector] Error type: {Type}, Recoverable: {Recoverable}",
                error.Type, error.IsRecoverable);

            // 2. Check if recoverable
            if (!error.IsRecoverable)
            {
                _logger.LogWarning("[SQL Corrector] Error is not auto-correctable");
                attempt.Success = false;
                attempt.Reasoning = "Error cannot be automatically corrected.";
                return attempt;
            }

            // 3. Build schema context for correction
            var schemaContextText = BuildSchemaContextForCorrection(schemaContext, error);

            // 4. Extract filter value from intent (for parameter replacement)
            // FIX: Convert object? to string? (handle array case)
            var filterObj = intent.Filters.FirstOrDefault()?.Value;

            var filterValue = filterObj switch
            {
                string s => s,

                JsonElement je when je.ValueKind == JsonValueKind.Array
                    => je.GetArrayLength().ToString(),

                JsonElement je
                    => je.ToString(),

                _ => filterObj?.ToString()
            };



            // 5. Generate corrected SQL
            var userPrompt = SqlCorrectionPrompt.BuildUserPrompt(
                originalSql,
                errorMessage,
                error.Type.ToString(),
                error.InvalidElement,
                schemaContextText,
                filterValue);  // ← PASS filter value

            var correctedSql = await _llmClient.CompleteWithSystemPromptAsync(
                _adapter.GetCorrectionSystemPrompt(),
                userPrompt,
                cancellationToken);

            // 6. Clean response
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
            attempt.Reasoning = $"Lỗi khi sửa: {ex.Message}";
            return attempt;
        }
    }

    private string BuildSchemaContextForCorrection(RetrievedSchemaContext context, SqlError error)
    {
        var schemaText = "";

        // Build COMPLETE schema info with EXACT column names
        foreach (var table in context.RelevantTables)
        {
            schemaText += $"\n========================================\n";
            schemaText += $"Table: [{table.Schema}].[{table.TableName}]\n";
            schemaText += $"========================================\n";
            schemaText += "Columns (USE THESE EXACT NAMES):\n";

            foreach (var col in table.Columns)
            {
                var pk = col.IsPrimaryKey ? " ← PRIMARY KEY" : "";
                var fk = col.IsForeignKey ? " ← FOREIGN KEY" : "";
                var nullable = col.IsNullable ? " NULL" : " NOT NULL";

                // Make it very explicit
                schemaText += $"  [{col.ColumnName}]  ({col.DataType}{nullable}){pk}{fk}\n";
            }

            schemaText += "\n";
        }

        // Add relationships with EXACT column names
        if (context.RelevantRelationships.Any())
        {
            schemaText += "========================================\n";
            schemaText += "RELATIONSHIPS (JOIN using these):\n";
            schemaText += "========================================\n";

            foreach (var rel in context.RelevantRelationships)
            {
                schemaText += $"  [{rel.FromTable}].[{rel.FromColumn}] → [{rel.ToTable}].[{rel.ToColumn}]\n";
            }
            schemaText += "\n";
        }

        // Add specific hints based on error
        if (error.Type == SqlErrorType.InvalidColumnName && !string.IsNullOrEmpty(error.InvalidElement))
        {
            schemaText += "========================================\n";
            schemaText += "ERROR DETECTED:\n";
            schemaText += "========================================\n";
            schemaText += $"Column '{error.InvalidElement}' DOES NOT EXIST!\n";
            schemaText += "You MUST use one of the EXACT column names listed above.\n";
            schemaText += "Check the column list carefully and use the EXACT spelling.\n\n";

            // Try to suggest similar columns
            var suggestions = FindSimilarColumns(error.InvalidElement, context);
            if (suggestions.Any())
            {
                schemaText += "Did you mean one of these?\n";
                foreach (var (table, column) in suggestions)
                {
                    schemaText += $"  - [{table}].[{column}]\n";
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
        var lowerInvalid = invalidColumn.ToLower().Replace("_", "");

        foreach (var table in context.RelevantTables)
        {
            foreach (var col in table.Columns)
            {
                var lowerCol = col.ColumnName.ToLower().Replace("_", "");

                // Check if similar
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

        // Show what changed
        if (originalSql != correctedSql)
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

        // Simple word-level diff
        var originalWords = original.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var correctedWords = corrected.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < Math.Min(originalWords.Length, correctedWords.Length); i++)
        {
            if (originalWords[i] != correctedWords[i])
            {
                changes.Add($"'{originalWords[i]}' → '{correctedWords[i]}'");
            }
        }

        return changes.Take(5).ToList(); // Limit to 5 changes
    }

    private string CleanSqlResponse(string sql)
    {
        // Remove markdown
        sql = Regex.Replace(sql, @"```sql\s*", "", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"```\s*", "", RegexOptions.IgnoreCase);

        // Remove common prefixes
        sql = Regex.Replace(sql, @"^SQL:\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"^Corrected SQL:\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

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

        // Don't retry if error is not recoverable
        if (!lastAttempt.Error.IsRecoverable)
        {
            _logger.LogWarning("[SQL Corrector] Error not recoverable, stopping retry");
            return false;
        }

        // Don't retry if we're producing the same SQL
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

