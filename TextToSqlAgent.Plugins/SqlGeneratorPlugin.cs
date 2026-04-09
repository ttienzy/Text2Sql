using System.ComponentModel;
using System.Text.Json;
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
        string? originalQuestion = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlGenerator] Generating SQL query for {Provider}...", _adapter.Provider);

        var schemaContext = BuildSchemaContext(intent.Target, schema);

        var userPrompt = SqlGenerationPrompt.BuildUserPrompt(
            intent.Intent.ToString(),
            intent.Target,
            schemaContext,
            intent.Filters.Select(f => $"{f.Field} {f.Operator} {ConvertFilterValue(f.Value)}").ToList(),
            intent.Metrics.Select(m => $"{m.Alias}: {m.Calculation}").ToList(),
            originalQuestion,
            intent.SelectColumns);

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
            return "Metadata query - list all tables in the database.";
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

        // Compact format: Table(col1 type1, col2 type2)
        var cols = string.Join(", ", table.Columns.Select(c =>
        {
            var pk = c.IsPrimaryKey ? " PK" : "";
            return $"{c.ColumnName} {c.DataType}{pk}";
        }));

        var context = $"{table.TableName}({cols})";

        // Add relationships in compact format
        var relationships = schema.Relationships
            .Where(r => r.FromTable.Contains(table.TableName) || r.ToTable.Contains(table.TableName))
            .ToList();

        if (relationships.Any())
        {
            var rels = string.Join(", ", relationships.Select(r =>
                $"{r.FromTable}.{r.FromColumn}→{r.ToTable}.{r.ToColumn}"));
            context += $"\nJOINs: {rels}";
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

    /// <summary>
    /// Validate SQL safety with context-aware rules per pipeline type
    /// ✅ NEW-1 FIX: Different validation rules for Query/Write/DDL pipelines
    /// </summary>
    [KernelFunction, Description("Validate SQL safety")]
    public bool ValidateSql(string sql, string pipelineType = "Query")
    {
        _logger.LogDebug("[Agent] Validating SQL for pipeline: {PipelineType}...", pipelineType);

        var upperSql = sql.ToUpper();

        // Route to appropriate validation based on pipeline type
        return pipelineType.ToLower() switch
        {
            "query" => ValidateQuerySql(upperSql),
            "write" => ValidateWriteSql(upperSql),
            "ddl" => ValidateDdlSql(upperSql),
            "forbidden" => false, // Always reject
            _ => ValidateQuerySql(upperSql) // Default to strictest
        };
    }

    /// <summary>
    /// Validate Query pipeline SQL - Only SELECT allowed
    /// </summary>
    private bool ValidateQuerySql(string upperSql)
    {
        // Dangerous keywords for Query pipeline
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
                _logger.LogWarning("[Agent] Query SQL contains dangerous keyword: {Keyword}", keyword);
                return false;
            }
        }

        // Must contain SELECT
        if (!upperSql.Contains("SELECT"))
        {
            _logger.LogWarning("[Agent] Query SQL does not contain SELECT");
            return false;
        }

        _logger.LogDebug("[Agent] Query SQL valid");
        return true;
    }

    /// <summary>
    /// Validate Write pipeline SQL - Allow INSERT, UPDATE but not DELETE, DROP
    /// </summary>
    private bool ValidateWriteSql(string upperSql)
    {
        // Forbidden keywords for Write pipeline
        var forbiddenKeywords = new[]
        {
            "DROP", "TRUNCATE", "EXEC", "EXECUTE",
            "SP_", "XP_", "GRANT", "REVOKE", "SHUTDOWN"
        };

        foreach (var keyword in forbiddenKeywords)
        {
            if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
            {
                _logger.LogWarning("[Agent] Write SQL contains forbidden keyword: {Keyword}", keyword);
                return false;
            }
        }

        // Must contain INSERT or UPDATE
        bool hasInsert = upperSql.Contains("INSERT");
        bool hasUpdate = upperSql.Contains("UPDATE");

        if (!hasInsert && !hasUpdate)
        {
            _logger.LogWarning("[Agent] Write SQL must contain INSERT or UPDATE");
            return false;
        }

        _logger.LogDebug("[Agent] Write SQL valid");
        return true;
    }

    /// <summary>
    /// Validate DDL pipeline SQL - Allow CREATE, ALTER but not DROP
    /// </summary>
    private bool ValidateDdlSql(string upperSql)
    {
        // Forbidden keywords for DDL pipeline
        var forbiddenKeywords = new[]
        {
            "DROP", "TRUNCATE", "DELETE", "EXEC", "EXECUTE",
            "SP_", "XP_", "GRANT", "REVOKE", "SHUTDOWN"
        };

        foreach (var keyword in forbiddenKeywords)
        {
            if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
            {
                _logger.LogWarning("[Agent] DDL SQL contains forbidden keyword: {Keyword}", keyword);
                return false;
            }
        }

        // Must contain CREATE or ALTER
        bool hasCreate = upperSql.Contains("CREATE");
        bool hasAlter = upperSql.Contains("ALTER");

        if (!hasCreate && !hasAlter)
        {
            _logger.LogWarning("[Agent] DDL SQL must contain CREATE or ALTER");
            return false;
        }

        _logger.LogDebug("[Agent] DDL SQL valid");
        return true;
    }

    [KernelFunction, Description("Add LIMIT/TOP if missing")]
    public string EnsureLimit(string sql, int defaultLimit = 100)
    {
        return _adapter.ApplyLimit(sql, defaultLimit);
    }
    [KernelFunction, Description("Generate SQL with RAG context and suggestions")]
    public async Task<SqlGenerationResult> GenerateSqlWithContextAsync(
    IntentAnalysis intent,
    RetrievedSchemaContext schemaContext,
    string? originalQuestion = null,
    List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory = null,
    CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlGenerator] Generating SQL query with RAG context for {Provider}...", _adapter.Provider);

        var schemaContextText = BuildEnhancedSchemaContext(schemaContext);

        var userPrompt = SqlGenerationPrompt.BuildUserPromptWithSuggestions(
            intent.Intent.ToString(),
            intent.Target,
            schemaContextText,
            intent.Filters.Select(f => $"{f.Field} {f.Operator} {ConvertFilterValue(f.Value)}").ToList(),
            intent.Metrics.Select(m => $"{m.Alias}: {m.Calculation}").ToList(),
            originalQuestion,
            intent.SelectColumns,
            conversationHistory);

        // Use enhanced system prompt for JSON output
        var systemPrompt = SqlGenerationPrompt.SystemPromptWithSuggestions;

        var response = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        var result = ParseSqlGenerationResult(response);

        _logger.LogDebug("[SqlGenerator] Generated SQL for {Provider} with {Count} suggestions",
            _adapter.Provider, result.SuggestedQueries.Count);

        return result;
    }

    /// <summary>
    /// Generate SQL with RAG context and streaming support (token-by-token)
    /// Emits SQL tokens via callback as they're generated by LLM
    /// </summary>
    [KernelFunction, Description("Generate SQL with RAG context and streaming")]
    public async Task<SqlGenerationResult> GenerateSqlWithContextStreamAsync(
        IntentAnalysis intent,
        RetrievedSchemaContext schemaContext,
        string? originalQuestion = null,
        List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory = null,
        Action<string>? tokenCallback = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlGenerator] Generating SQL query with streaming for {Provider}...", _adapter.Provider);

        var schemaContextText = BuildEnhancedSchemaContext(schemaContext);

        var userPrompt = SqlGenerationPrompt.BuildUserPromptWithSuggestions(
            intent.Intent.ToString(),
            intent.Target,
            schemaContextText,
            intent.Filters.Select(f => $"{f.Field} {f.Operator} {ConvertFilterValue(f.Value)}").ToList(),
            intent.Metrics.Select(m => $"{m.Alias}: {m.Calculation}").ToList(),
            originalQuestion,
            intent.SelectColumns,
            conversationHistory);

        var systemPrompt = SqlGenerationPrompt.SystemPromptWithSuggestions;

        // ✅ Use streaming API with token callback
        var response = await _llmClient.CompleteWithSystemPromptStreamAsync(
            systemPrompt,
            userPrompt,
            tokenCallback,
            cancellationToken);

        var result = ParseSqlGenerationResult(response);

        _logger.LogDebug("[SqlGenerator] Streaming complete for {Provider} with {Count} suggestions",
            _adapter.Provider, result.SuggestedQueries.Count);

        return result;
    }

    /// <summary>
    /// Legacy method for backward compatibility - returns only SQL string
    /// </summary>
    [KernelFunction, Description("Generate SQL with RAG context (legacy)")]
    public async Task<string> GenerateSqlWithContextLegacyAsync(
    IntentAnalysis intent,
    RetrievedSchemaContext schemaContext,
    string? originalQuestion = null,
    List<TextToSqlAgent.Infrastructure.Entities.Message>? conversationHistory = null,
    CancellationToken cancellationToken = default)
    {
        var result = await GenerateSqlWithContextAsync(intent, schemaContext, originalQuestion, conversationHistory, cancellationToken);
        return result.Sql;
    }



    private string BuildEnhancedSchemaContext(RetrievedSchemaContext context)
    {
        // Compact format: Table(col1 type1, col2 type2)
        var tables = new List<string>();
        foreach (var table in context.RelevantTables)
        {
            var cols = string.Join(", ", table.Columns.Select(c =>
            {
                var pk = c.IsPrimaryKey ? " PK" : "";
                var fk = c.IsForeignKey ? " FK" : "";
                return $"{c.ColumnName} {c.DataType}{pk}{fk}";
            }));
            tables.Add($"{table.TableName}({cols})");
        }

        var schemaText = string.Join("\n", tables);

        // Relationships in compact format
        if (context.RelevantRelationships.Any())
        {
            var rels = string.Join(", ", context.RelevantRelationships.Select(r =>
                $"{r.FromTable}.{r.FromColumn}→{r.ToTable}.{r.ToColumn}"));
            schemaText += $"\nJOINs: {rels}";
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

    private SqlGenerationResult ParseSqlGenerationResult(string raw)
    {
        try
        {
            // ✅ Log the raw response for debugging
            _logger.LogDebug("[SqlGenerator] Raw LLM response:\n{Raw}", raw);

            // Strip markdown code blocks if present
            var cleaned = raw
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            // ✅ Find JSON block if LLM adds extra text
            var jsonStart = cleaned.IndexOf('{');
            var jsonEnd = cleaned.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                cleaned = cleaned[jsonStart..(jsonEnd + 1)];
            }

            // ✅ Log the cleaned response
            _logger.LogDebug("[SqlGenerator] Cleaned JSON: {Response}", cleaned.Length > 300 ? cleaned.Substring(0, 300) + "..." : cleaned);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<SqlGenerationResult>(cleaned, options);

            if (result == null || string.IsNullOrWhiteSpace(result.Sql))
            {
                _logger.LogWarning("[SqlGenerator] JSON parse ok but SQL empty, using raw");

                // Fallback: treat entire response as SQL (backward compatibility)
                return new SqlGenerationResult
                {
                    Sql = CleanSqlResponse(cleaned),
                    SuggestedQueries = new List<string>()
                };
            }

            // Clean the SQL from the parsed result
            result.Sql = CleanSqlResponse(result.Sql);

            // ✅ Handle Case C: LLM uses alternative keys like "suggestions" instead of "suggested_queries"
            if (result.SuggestedQueries.Count == 0)
            {
                try
                {
                    // Try parsing manually with JsonDocument
                    using var doc = JsonDocument.Parse(cleaned);
                    var root = doc.RootElement;

                    // Check alternative keys LLM commonly uses
                    string[] altKeys = ["suggestions", "follow_up", "related_queries", "next_queries", "followup_queries"];
                    foreach (var key in altKeys)
                    {
                        if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        {
                            result.SuggestedQueries = arr
                                .EnumerateArray()
                                .Select(e => e.GetString() ?? "")
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList();

                            if (result.SuggestedQueries.Count > 0)
                            {
                                _logger.LogDebug("[SqlGenerator] Found {Count} suggestions under key '{Key}'", result.SuggestedQueries.Count, key);
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[SqlGenerator] Failed to parse alternative suggestion keys");
                }
            }

            // ✅ Log detailed suggestion info
            _logger.LogInformation(
                "[SqlGenerator] ✅ Parsed SQL + {Count} suggestions: [{Suggestions}]",
                result.SuggestedQueries.Count,
                string.Join(", ", result.SuggestedQueries.Take(3).Select(s => $"\"{s}\"")));

            // ✅ Debug: Log if no suggestions found
            if (result.SuggestedQueries.Count == 0)
            {
                _logger.LogWarning("[SqlGenerator] ⚠️ No suggested queries found in LLM response. Raw response: {Raw}",
                    raw.Length > 500 ? raw.Substring(0, 500) + "..." : raw);
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[SqlGenerator] JSON parse failed, using raw as SQL. Raw response: {Raw}",
                raw.Length > 200 ? raw.Substring(0, 200) + "..." : raw);

            return new SqlGenerationResult
            {
                Sql = CleanSqlResponse(raw.Trim()),
                SuggestedQueries = new List<string>()
            };
        }
    }

    /// <summary>
    /// Generate contextual suggestions based on actual query results
    /// </summary>
    [KernelFunction, Description("Generate contextual suggestions based on actual query results")]
    public async Task<List<string>> GenerateContextualSuggestionsAsync(
        string originalQuestion,
        string sqlQuery,
        SqlExecutionResult queryResult,
        IntentAnalysis intent,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SqlGenerator] Generating contextual suggestions based on actual results...");

        try
        {
            var prompt = BuildContextualSuggestionsPrompt(originalQuestion, sqlQuery, queryResult, intent);

            var response = await _llmClient.CompleteWithSystemPromptAsync(
                GetContextualSuggestionsSystemPrompt(),
                prompt,
                cancellationToken);

            var suggestions = ParseSuggestionsFromResponse(response);

            _logger.LogDebug("[SqlGenerator] Generated {Count} contextual suggestions", suggestions.Count);

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SqlGenerator] Failed to generate contextual suggestions, using fallback");
            return GenerateFallbackSuggestions(intent, originalQuestion);
        }
    }

    private string BuildContextualSuggestionsPrompt(string originalQuestion, string sqlQuery, SqlExecutionResult queryResult, IntentAnalysis intent)
    {
        var prompt = $@"
User asked: ""{originalQuestion}""

SQL executed: {sqlQuery}

Query Results:
- Row count: {queryResult.RowCount}
- Columns: {string.Join(", ", queryResult.Columns)}";

        // Add sample data if available
        if (queryResult.Rows?.Count > 0)
        {
            prompt += "\n- Sample data (first 3 rows):";
            var sampleRows = queryResult.Rows.Take(3);
            foreach (var row in sampleRows)
            {
                var values = queryResult.Columns.Select(col =>
                    row.ContainsKey(col) ? row[col]?.ToString() ?? "null" : "null");
                prompt += $"\n  [{string.Join(", ", values)}]";
            }
        }

        prompt += $@"

Based on the ACTUAL RESULTS above, generate 3 natural follow-up questions that a business user would realistically ask.

Requirements:
- Use Vietnamese language (same as user's question)
- Be specific to the actual data returned
- Focus on business insights and next steps
- Make them actionable and interesting
- Consider the data patterns, values, and trends visible in results

Examples of good contextual suggestions:
- If showing top customers → ""Khách hàng này mua sản phẩm gì nhiều nhất?""
- If showing revenue by month → ""Tháng nào có doanh thu thấp nhất và tại sao?""
- If showing product sales → ""So sánh với cùng kỳ năm trước như thế nào?""

Return ONLY a JSON array of 3 strings:
[""suggestion 1"", ""suggestion 2"", ""suggestion 3""]";

        return prompt;
    }

    private string GetContextualSuggestionsSystemPrompt()
    {
        return @"You are a business intelligence expert who helps users explore their data.
Your job is to suggest natural, contextual follow-up questions based on actual query results.

CRITICAL RULES:
1. Analyze the ACTUAL DATA returned, not just the query
2. Suggest questions that build on what the user just discovered
3. Use Vietnamese language naturally
4. Focus on business insights and actionable next steps
5. Be specific to the data patterns you see
6. Return ONLY a JSON array of exactly 3 strings
7. No explanations, no markdown, just the JSON array

The suggestions should feel like what a curious business analyst would ask next.";
    }

    private List<string> ParseSuggestionsFromResponse(string response)
    {
        try
        {
            // Clean the response
            var cleaned = response.Trim();

            // Remove any markdown or extra text, find JSON array
            var jsonStart = cleaned.IndexOf('[');
            var jsonEnd = cleaned.LastIndexOf(']');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                cleaned = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            var suggestions = JsonSerializer.Deserialize<List<string>>(cleaned);
            return suggestions?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SqlGenerator] Failed to parse suggestions from response: {Response}", response);
            return new List<string>();
        }
    }

    private List<string> GenerateFallbackSuggestions(IntentAnalysis intent, string originalQuestion)
    {
        // Simple fallback based on intent
        var isVietnamese = originalQuestion.Contains("hiển thị") || originalQuestion.Contains("lấy") ||
                          originalQuestion.Contains("đếm") || originalQuestion.Contains("tổng");

        if (isVietnamese)
        {
            return new List<string>
            {
                "Xem chi tiết dữ liệu này",
                "So sánh với thời kỳ khác",
                "Phân tích xu hướng theo thời gian"
            };
        }

        return new List<string>
        {
            "Show more details about this data",
            "Compare with other periods",
            "Analyze trends over time"
        };
    }

}
