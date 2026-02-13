using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Plugins;

public class SqlGeneratorPlugin
{
    private readonly ILLMClient _llmClient;
    private readonly IDatabaseAdapter _adapter;
    private readonly ILogger<SqlGeneratorPlugin> _logger;

    public SqlGeneratorPlugin(
        ILLMClient llmClient,
        IDatabaseAdapter adapter,
        ILogger<SqlGeneratorPlugin> logger)
    {
        _llmClient = llmClient;
        _adapter = adapter;
        _logger = logger;
    }


    [KernelFunction, Description("Generate SQL query from intent and schema")]
    public async Task<string> GenerateSqlAsync(
        IntentAnalysis intent,
        DatabaseSchema schema,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlGenerator] Generating SQL query for {Provider}...", _adapter.Provider);

        var schemaContext = BuildSchemaContext(intent.Target, schema);

        var userPrompt = SqlGenerationPrompt.BuildUserPrompt(
            intent.Intent.ToString(),
            intent.Target,
            schemaContext,
            intent.Filters.Select(f => $"{f.Field} {f.Operator} {ConvertFilterValue(f.Value)}").ToList(),
            intent.Metrics);

        // Use adapter's database-specific system prompt
        var systemPrompt = _adapter.GetSystemPrompt();
        
        var sql = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        // Clean the SQL
        sql = CleanSqlResponse(sql);

        _logger.LogDebug("[SqlGenerator] Generated SQL for {Provider}: {SQL}", _adapter.Provider, sql);

        return sql;
    }



    private string BuildSchemaContext(string targetTable, DatabaseSchema schema)
    {
        // Special case: SCHEMA queries
        if (targetTable.Equals("TABLES", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("SCHEMA", StringComparison.OrdinalIgnoreCase))
        {
            return "Metadata query - use INFORMATION_SCHEMA.TABLES";
        }

        // Find the target table
        var table = schema.Tables.FirstOrDefault(t =>
            t.TableName.Equals(targetTable, StringComparison.OrdinalIgnoreCase));

        if (table == null)
        {
            // Try to find related tables
            var similarTables = schema.Tables
                .Where(t => t.TableName.Contains(targetTable, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (similarTables.Any())
            {
                _logger.LogWarning(
                    "[Agent] Table '{Target}' not found, suggestion: {Similar}",
                    targetTable,
                    string.Join(", ", similarTables.Select(t => t.TableName)));

                table = similarTables.First();
            }
            else
            {
                return $"Table '{targetTable}' not found in schema";
            }
        }

        // Build context with columns and relationships
        var context = $"Table: [{table.Schema}].[{table.TableName}]\n";
        context += "Columns:\n";

        foreach (var col in table.Columns)
        {
            var pk = col.IsPrimaryKey ? " (PK)" : "";
            var nullable = col.IsNullable ? " NULL" : " NOT NULL";
            context += $"  - [{col.ColumnName}] {col.DataType}{nullable}{pk}\n";
        }

        // Add relationships
        var relationships = schema.Relationships
            .Where(r => r.FromTable.Contains(table.TableName) || r.ToTable.Contains(table.TableName))
            .ToList();

        if (relationships.Any())
        {
            context += "\nRelationships:\n";
            foreach (var rel in relationships)
            {
                context += $"  - {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}\n";
            }
        }

        return context;
    }

    private string CleanSqlResponse(string sql)
    {
        // Remove markdown code blocks
        sql = Regex.Replace(sql, @"```sql\s*", "", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"```\s*", "", RegexOptions.IgnoreCase);

        // Remove common prefixes
        sql = Regex.Replace(sql, @"^SQL:\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"^Query:\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Trim
        sql = sql.Trim();

        // Ensure it ends with semicolon is optional, remove if exists for consistency
        sql = sql.TrimEnd(';');

        return sql;
    }

    [KernelFunction, Description("Validate SQL safety")]
    public bool ValidateSql(string sql)
    {
        _logger.LogDebug("[Agent] Validating SQL...");

        // Convert to uppercase for checking
        var upperSql = sql.ToUpper();

        // Dangerous keywords
        var dangerousKeywords = new[]
        {
            "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE",
            "INSERT", "UPDATE", "EXEC", "EXECUTE", "SP_",
            "XP_", "GRANT", "REVOKE", "SHUTDOWN"
        };

        foreach (var keyword in dangerousKeywords)
        {
            if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
            {
                _logger.LogWarning("[Agent] SQL contains dangerous keyword: {Keyword}", keyword);
                return false;
            }
        }

        // Must contain SELECT
        if (!upperSql.Contains("SELECT"))
        {
            _logger.LogWarning("[Agent] SQL does not contain SELECT");
            return false;
        }

        _logger.LogDebug("[Agent] SQL valid");
        return true;
    }

    [KernelFunction, Description("Add LIMIT if missing")]
    public string EnsureLimit(string sql, int defaultLimit = 100)
    {
        var upperSql = sql.ToUpper();

        // Check if already has TOP or OFFSET/FETCH
        if (upperSql.Contains("TOP ") || upperSql.Contains("OFFSET") || upperSql.Contains("FETCH"))
        {
            return sql;
        }

        // Check if it's an aggregate query (has GROUP BY or aggregate functions)
        if (upperSql.Contains("GROUP BY") ||
            upperSql.Contains("COUNT(") ||
            upperSql.Contains("SUM(") ||
            upperSql.Contains("AVG("))
        {
            // Don't add TOP to aggregate queries
            return sql;
        }

        // Add TOP after SELECT
        var modified = Regex.Replace(
            sql,
            @"SELECT\s+",
            $"SELECT TOP {defaultLimit} ",
            RegexOptions.IgnoreCase);

        _logger.LogDebug("[Agent] Added TOP {Limit}", defaultLimit);

        return modified;
    }
    [KernelFunction, Description("Generate SQL with RAG context")]
    public async Task<string> GenerateSqlWithContextAsync(
    IntentAnalysis intent,
    RetrievedSchemaContext schemaContext,
    CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlGenerator] Generating SQL query with RAG context for {Provider}...", _adapter.Provider);

        var schemaContextText = BuildEnhancedSchemaContext(schemaContext);

        var userPrompt = SqlGenerationPrompt.BuildUserPrompt(
            intent.Intent.ToString(),
            intent.Target,
            schemaContextText,
            intent.Filters.Select(f => $"{f.Field} {f.Operator} {ConvertFilterValue(f.Value)}").ToList(),
            intent.Metrics);

        // Use adapter's database-specific system prompt
        var systemPrompt = _adapter.GetSystemPrompt();
        
        var sql = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        sql = CleanSqlResponse(sql);

        _logger.LogDebug("[SqlGenerator] Generated SQL for {Provider}: {SQL}", _adapter.Provider, sql);

        return sql;
    }



    private string BuildEnhancedSchemaContext(RetrievedSchemaContext context)
    {
        var schemaText = "";

        // All relevant tables with full details
        foreach (var table in context.RelevantTables)
        {
            schemaText += $"\nTable: [{table.Schema}].[{table.TableName}]\n";
            schemaText += "Columns:\n";

            foreach (var col in table.Columns)
            {
                var pk = col.IsPrimaryKey ? " (PK)" : "";
                var fk = col.IsForeignKey ? " (FK)" : "";
                var nullable = col.IsNullable ? " NULL" : " NOT NULL";

                schemaText += $"  - [{col.ColumnName}] {col.DataType}{nullable}{pk}{fk}\n";
            }
        }

        // Relationships
        if (context.RelevantRelationships.Any())
        {
            schemaText += "\nRelationships:\n";
            foreach (var rel in context.RelevantRelationships)
            {
                schemaText += $"  - {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}\n";
            }
        }

        return schemaText;
    }

    // FIX: Helper method to convert filter value to string
    private static string ConvertFilterValue(object? value)
    {
        return value switch
        {
            string s => s,
            System.Text.Json.JsonElement je => je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Array => string.Join(", ", je.EnumerateArray().Select(x => x.GetString() ?? "")),
                System.Text.Json.JsonValueKind.String => je.GetString() ?? "",
                _ => je.GetRawText()
            },
            _ => value?.ToString() ?? ""
        };
    }
}
