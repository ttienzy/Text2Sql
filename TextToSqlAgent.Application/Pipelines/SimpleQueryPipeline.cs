namespace TextToSqlAgent.Application.Pipelines;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;
using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Simple Query Pipeline - handles 70% of queries (single table, no joins/aggregation)
/// Target: 3-5 seconds with 2-3 LLM calls
/// 
/// Flow:
/// 1. Get Schema from Cache (0 LLM calls - already cached)
/// 2. Generate SQL - 1 LLM call (direct, no "think" phase)
/// 3. Validate SQL - Rule-based (no LLM)
/// 4. Execute SQL - direct DB execution
/// 5. Format Result - 1 LLM call (small) or template-based
/// </summary>
public class SimpleQueryPipeline : ISimpleQueryPipeline
{
    private readonly ISchemaCache _schemaCache;
    private readonly ILLMClient _llmClient;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ILogger<SimpleQueryPipeline> _logger;

    // Keywords that indicate this might NOT be a simple query
    private static readonly string[] ComplexKeywords =
    {
        "join", "left", "right", "inner", "outer", "full",
        "sum", "average", "avg", "count", "total", "tổng", "trung bình", "đếm",
        "top", "rank", "order by", "sắp xếp", "xếp hạng",
        "trend", "xu hướng", "so sánh", "compare",
        "month", "year", "day", "tháng", "năm", "ngày",
        "group by", "having", "distinct"
    };

    // SQL validation - dangerous keywords that should not be allowed
    private static readonly string[] ForbiddenKeywords =
    {
        "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE", "EXEC", "EXECUTE"
    };

    public SimpleQueryPipeline(
        ISchemaCache schemaCache,
        ILLMClient llmClient,
        ISqlExecutor sqlExecutor,
        ILogger<SimpleQueryPipeline> logger)
    {
        _schemaCache = schemaCache;
        _llmClient = llmClient;
        _sqlExecutor = sqlExecutor;
        _logger = logger;
    }

    public bool CanHandle(string query, Application.Routing.QueryComplexity complexity)
    {
        // Only handle Simple complexity
        if (complexity != Application.Routing.QueryComplexity.Simple)
            return false;

        // Double-check: if query has complex keywords, don't handle
        var lowerQuery = query.ToLowerInvariant();
        return !ComplexKeywords.Any(k => lowerQuery.Contains(k.ToLowerInvariant()));
    }

    public async Task<QueryResult> ExecuteAsync(SimpleQueryRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new QueryResult
        {
            Complexity = Application.Routing.QueryComplexity.Simple,
            ProcessingSteps = new List<string>()
        };

        try
        {
            _logger.LogInformation("[SimpleQueryPipeline] Starting execution for query: {Query}", request.Query);

            // Step 1: Get Schema from Cache (0 LLM calls)
            result.ProcessingSteps.Add("Get schema from cache");
            var schema = await _schemaCache.GetAsync(request.ConnectionId, ct);

            if (schema == null)
            {
                result.Success = false;
                result.ErrorMessage = "Schema not found. Please reconnect to the database.";
                return result;
            }

            // Step 2: Generate SQL - 1 LLM call (minimal prompt)
            result.ProcessingSteps.Add("Generate SQL with LLM");
            var sql = await GenerateSqlAsync(request.Query, schema, ct);
            result.SqlGenerated = sql;
            result.LlmCalls = 1;

            _logger.LogInformation("[SimpleQueryPipeline] Generated SQL: {Sql}", sql);

            // Step 3: Validate SQL - Rule-based (no LLM)
            result.ProcessingSteps.Add("Validate SQL");
            var validation = ValidateSql(sql);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.ErrorMessage = validation.ErrorMessage;
                return result;
            }

            // Apply LIMIT if not present
            sql = EnsureLimit(sql, request.MaxRows);

            // Step 4: Execute SQL - direct DB execution
            result.ProcessingSteps.Add("Execute SQL");
            var executionResult = await _sqlExecutor.ExecuteAsync(sql, ct);
            result.QueryResultData = executionResult;

            if (!executionResult.Success)
            {
                // Auto-escalate to Medium if execution fails
                result.WasEscalated = true;
                result.EscalationReason = $"SQL execution failed: {executionResult.ErrorMessage}";
                _logger.LogWarning("[SimpleQueryPipeline] SQL execution failed, escalating: {Error}", executionResult.ErrorMessage);

                // Still return failure but with escalation info
                result.Success = false;
                result.ErrorMessage = executionResult.ErrorMessage;
                return result;
            }

            // Check if returned 0 rows - might be wrong query
            if (executionResult.Rows == null || executionResult.Rows.Count == 0)
            {
                // Auto-escalate to Medium if 0 rows returned
                result.WasEscalated = true;
                result.EscalationReason = "Query returned 0 rows - might need JOIN or different filter";
                _logger.LogWarning("[SimpleQueryPipeline] Query returned 0 rows, escalating to Medium");
            }

            // ✅ IMPROVED: Check if SQL has JOIN - always escalate instead of continuing
            if (sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase))
            {
                result.WasEscalated = true;
                result.EscalationReason = "Query requires JOIN - escalating to Medium pipeline for better handling";
                result.Success = false;
                _logger.LogWarning("[SimpleQueryPipeline] JOIN detected in SQL, escalating to Medium");

                stopwatch.Stop();
                result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }

            // Check for aggregation functions
            if (ContainsAggregation(sql))
            {
                result.WasEscalated = true;
                result.EscalationReason = "Query contains aggregation - escalating to Medium pipeline";
                result.Success = false;
                _logger.LogWarning("[SimpleQueryPipeline] Aggregation detected, escalating to Medium");

                stopwatch.Stop();
                result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }

            // Step 5: Format Result - 1 LLM call (small) or template-based
            result.ProcessingSteps.Add("Format result");
            if (request.UseLlmFormatting)
            {
                result.FormattedAnswer = await FormatWithLlmAsync(request.Query, executionResult, ct);
                result.LlmCalls++;
            }
            else
            {
                result.FormattedAnswer = FormatWithTemplate(request.Query, executionResult);
            }

            result.Success = true;
            _logger.LogInformation("[SimpleQueryPipeline] Execution completed successfully");

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SimpleQueryPipeline] Error executing query");

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.WasEscalated = true;
            result.EscalationReason = $"Exception: {ex.Message}";

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
    }

    private async Task<string> GenerateSqlAsync(string query, DatabaseSchema schema, CancellationToken ct)
    {
        // Build minimal prompt with schema
        var schemaDescription = BuildSchemaDescription(schema);

        // OPTIMIZED: Use JSON output for reliable parsing
        var systemPrompt = "You are a SQL expert. " +
            "Return ONLY valid JSON, no explanation, no markdown, no code blocks. " +
            "JSON structure: {\"sql\": \"SQL statement\", \"confidence\": 0.95} " +
            "Rules: Only SELECT, no INSERT/UPDATE/DELETE/DROP/TRUNCATE. " +
            "Only 1 table, no JOIN. No aggregation (SUM, COUNT, AVG). " +
            "No subqueries. Example: {\"sql\": \"SELECT * FROM Customers WHERE CustomerID = 1\", \"confidence\": 0.95}";

        var userPrompt = $"Schema: {schemaDescription}\nQuestion: {query}\n\nReturn JSON now:";

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

        // Extract SQL from JSON response
        return ExtractSqlFromJson(response);
    }

    /// <summary>
    /// Extract SQL from JSON response with better parsing
    /// </summary>
    private string ExtractSqlFromJson(string response)
    {
        try
        {
            // Try to parse as JSON first
            var jsonDoc = JsonDocument.Parse(response);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("sql", out var sqlElement))
            {
                return sqlElement.GetString()?.Trim() ?? "";
            }

            // Try alternative property names
            if (root.TryGetProperty("query", out sqlElement) ||
                root.TryGetProperty("statement", out sqlElement))
            {
                return sqlElement.GetString()?.Trim() ?? "";
            }
        }
        catch (Exception)
        {
            // Not JSON, try markdown extraction
        }

        // Fallback to original markdown extraction
        return ExtractSql(response);
    }

    private string BuildSchemaDescription(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        // Include all relevant tables (no 10-table limit)
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"{table.TableName}: {string.Join(", ", table.Columns.Select(c => c.ColumnName))}");
        }

        return sb.ToString();
    }

    private string ExtractSql(string response)
    {
        // Try to extract SQL from markdown code blocks
        var startIndex = response.IndexOf("```sql", StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            startIndex += 6;
            var endIndex = response.IndexOf("```", startIndex);
            if (endIndex > startIndex)
            {
                return response.Substring(startIndex, endIndex - startIndex).Trim();
            }
        }

        // Try just code blocks
        startIndex = response.IndexOf("```", StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            startIndex += 3;
            var endIndex = response.IndexOf("```", startIndex);
            if (endIndex > startIndex)
            {
                return response.Substring(startIndex, endIndex - startIndex).Trim();
            }
        }

        // Return as-is if no code block found
        return response.Trim();
    }

    private (bool IsValid, string? ErrorMessage) ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "Generated SQL is empty");

        // Check for forbidden keywords
        var upperSql = sql.ToUpperInvariant();
        foreach (var keyword in ForbiddenKeywords)
        {
            if (upperSql.Contains(keyword))
            {
                return (false, $"Forbidden keyword detected: {keyword}");
            }
        }

        // Must start with SELECT
        if (!upperSql.TrimStart().StartsWith("SELECT"))
        {
            return (false, "Only SELECT queries are allowed");
        }

        // ✅ REMOVED: Don't reject JOINs/subqueries here - let escalation logic handle it
        // These checks are now done BEFORE execution to trigger escalation

        return (true, null);
    }

    private string EnsureLimit(string sql, int maxRows)
    {
        var upperSql = sql.ToUpperInvariant();
        if (!upperSql.Contains("LIMIT"))
        {
            return $"{sql} LIMIT {maxRows}";
        }
        return sql;
    }

    private bool ContainsComplexKeywords(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        return ComplexKeywords.Any(k => lowerQuery.Contains(k.ToLowerInvariant()));
    }

    private bool ContainsAggregation(string sql)
    {
        var upperSql = sql.ToUpperInvariant();
        var aggregationKeywords = new[] { "SUM(", "AVG(", "COUNT(", "MAX(", "MIN(", "GROUP BY" };
        return aggregationKeywords.Any(k => upperSql.Contains(k));
    }

    private async Task<string> FormatWithLlmAsync(string query, SqlExecutionResult executionResult, CancellationToken ct)
    {
        var systemPrompt = "You are a database assistant. Answer in Vietnamese, concise and friendly.";

        var dataJson = executionResult.Rows.Count > 0
            ? JsonSerializer.Serialize(executionResult.Rows.Take(10))
            : "No data";

        var userPrompt = $"Question: {query}\nResult: {dataJson}\nAnswer:";

        return await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);
    }

    private string FormatWithTemplate(string query, SqlExecutionResult executionResult)
    {
        if (executionResult.Rows == null || executionResult.Rows.Count == 0)
        {
            return "Không có dữ liệu trả về.";
        }

        var rowCount = executionResult.RowCount;

        return $"Tìm thấy {rowCount} kết quả:";
    }

    private string ExtractTableName(string sql)
    {
        // Simple extraction of table name from SQL
        var upperSql = sql.ToUpperInvariant();
        var fromIndex = upperSql.IndexOf("FROM");
        if (fromIndex >= 0)
        {
            var tableStart = fromIndex + 5;
            var whereIndex = upperSql.IndexOf("WHERE", fromIndex);
            var endIndex = whereIndex > 0 ? whereIndex : sql.Length;

            var tableName = sql.Substring(tableStart, endIndex - tableStart).Trim();

            // Remove aliases
            if (tableName.Contains(" "))
                tableName = tableName.Split(' ')[0];

            return tableName;
        }

        return "Unknown";
    }
}
