using Microsoft.Extensions.Logging;
using System.Text.Json;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Service for generating smart query suggestions based on table structure
/// </summary>
public class QuerySuggestionService
{
    private readonly ILLMClient _llm;
    private readonly ILogger<QuerySuggestionService> _logger;

    public QuerySuggestionService(
        ILLMClient llm,
        ILogger<QuerySuggestionService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<List<QuerySuggestion>> GenerateSuggestionsAsync(
        EnhancedTableInfo table,
        List<EnhancedTableInfo> relatedTables,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildPrompt(table, relatedTables);

            var response = await _llm.CompleteAsync(prompt, cancellationToken: cancellationToken);

            var suggestions = ParseSuggestions(response);

            _logger.LogInformation(
                "[QuerySuggestion] Generated {Count} suggestions for table {TableName}",
                suggestions.Count,
                table.TableName);

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuerySuggestion] Error generating suggestions for {TableName}", table.TableName);
            return GetFallbackSuggestions(table);
        }
    }

    private string BuildPrompt(EnhancedTableInfo table, List<EnhancedTableInfo> relatedTables)
    {
        var columnsInfo = string.Join("\n", table.Columns.Select(c =>
            $"  - {c.ColumnName} ({c.DataType}){(c.IsNullable ? " NULL" : " NOT NULL")}{(c.IsPrimaryKey ? " PK" : "")}{(c.IsForeignKey ? " FK" : "")}"));

        var relationshipsInfo = relatedTables.Any()
            ? "\n\nRelated Tables:\n" + string.Join("\n", relatedTables.Select(t => $"  - {t.TableName} (via FK)"))
            : "";

        return $@"You are a SQL expert. Generate 5-7 useful SQL query suggestions for the following table.

Table: {table.Schema}.{table.TableName}
Role: {table.Role}
Row Count: {table.RowCount:N0}
Columns:
{columnsInfo}{relationshipsInfo}

Generate queries in these categories:
1. Basic Queries (SELECT, COUNT, DISTINCT)
2. Analytics (GROUP BY, aggregations)
3. Data Quality (NULL checks, duplicates)
4. Relationships (JOINs with related tables)

Return ONLY a JSON array with this exact structure:
[
  {{
    ""title"": ""Query title"",
    ""description"": ""What this query does"",
    ""query"": ""SELECT ... FROM ..."",
    ""category"": ""basic|analytics|quality|relationships"",
    ""complexity"": ""low|medium|high""
  }}
]

Make queries practical and useful for developers. Use proper SQL Server syntax.";
    }

    private List<QuerySuggestion> ParseSuggestions(string response)
    {
        try
        {
            // Clean markdown code blocks
            var cleaned = response.Trim();
            if (cleaned.StartsWith("```json"))
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            var suggestions = JsonSerializer.Deserialize<List<QuerySuggestion>>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return suggestions ?? new List<QuerySuggestion>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[QuerySuggestion] Failed to parse LLM response, using fallback");
            return new List<QuerySuggestion>();
        }
    }

    private List<QuerySuggestion> GetFallbackSuggestions(EnhancedTableInfo table)
    {
        var suggestions = new List<QuerySuggestion>
        {
            new QuerySuggestion
            {
                Title = "View all records",
                Description = $"Retrieve all data from {table.TableName}",
                Query = $"SELECT TOP 100 * FROM [{table.Schema}].[{table.TableName}];",
                Category = "basic",
                Complexity = "low"
            },
            new QuerySuggestion
            {
                Title = "Count total records",
                Description = $"Get the total number of rows in {table.TableName}",
                Query = $"SELECT COUNT(*) AS TotalRecords FROM [{table.Schema}].[{table.TableName}];",
                Category = "basic",
                Complexity = "low"
            }
        };

        // Add PK-based query if available
        if (table.PrimaryKeys.Any())
        {
            var pkColumn = table.PrimaryKeys.First();
            suggestions.Add(new QuerySuggestion
            {
                Title = "Find by primary key",
                Description = $"Retrieve a specific record by {pkColumn}",
                Query = $"SELECT * FROM [{table.Schema}].[{table.TableName}] WHERE [{pkColumn}] = ?;",
                Category = "basic",
                Complexity = "low"
            });
        }

        // Add NULL check if there are nullable columns
        var nullableColumns = table.Columns.Where(c => c.IsNullable).ToList();
        if (nullableColumns.Any())
        {
            var col = nullableColumns.First();
            suggestions.Add(new QuerySuggestion
            {
                Title = "Check for NULL values",
                Description = $"Find records with NULL in {col.ColumnName}",
                Query = $"SELECT * FROM [{table.Schema}].[{table.TableName}] WHERE [{col.ColumnName}] IS NULL;",
                Category = "quality",
                Complexity = "low"
            });
        }

        return suggestions;
    }
}

/// <summary>
/// Query suggestion model
/// </summary>
public class QuerySuggestion
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Category { get; set; } = "basic"; // basic, analytics, quality, relationships
    public string Complexity { get; set; } = "low"; // low, medium, high
}
