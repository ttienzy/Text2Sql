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
/// Medium Query Pipeline - handles 25% of queries (JOINs, aggregation, time filters, ranking)
/// Target: 15-20 seconds with 5-8 LLM calls
/// 
/// Flow:
/// 1. Get Schema from Cache (0 LLM calls)
/// 2. Think + Generate SQL - Combined in 1 LLM call
/// 3. Execute SQL
/// 4. Validate Result - smarter validation (check row counts, nulls, ranges)
/// 5. Self-Correction - max 1 time only
/// 6. Format Answer
/// 7. Auto-escalate to Complex if needed
/// </summary>
public class MediumQueryPipeline : IMediumQueryPipeline
{
    private readonly ISchemaCache _schemaCache;
    private readonly ILLMClient _llmClient;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ILogger<MediumQueryPipeline> _logger;

    // SQL validation - dangerous keywords that should not be allowed
    private static readonly string[] ForbiddenKeywords =
    {
        "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE", "EXEC", "EXECUTE"
    };

    // Maximum self-correction attempts
    private const int MaxSelfCorrectionAttempts = 1;

    // Escalation thresholds
    private const double HighAmbiguityThreshold = 0.7;
    private const int MaxAllowedSubqueries = 1;

    public MediumQueryPipeline(
        ISchemaCache schemaCache,
        ILLMClient llmClient,
        ISqlExecutor sqlExecutor,
        ILogger<MediumQueryPipeline> logger)
    {
        _schemaCache = schemaCache;
        _llmClient = llmClient;
        _sqlExecutor = sqlExecutor;
        _logger = logger;
    }

    public bool CanHandle(QueryComplexity complexity)
    {
        // Only handle Medium complexity
        return complexity == QueryComplexity.Medium;
    }

    public async Task<QueryResult> ExecuteAsync(MediumQueryRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new QueryResult
        {
            Complexity = QueryComplexity.Medium,
            ProcessingSteps = new List<string>()
        };

        try
        {
            _logger.LogInformation("[MediumQueryPipeline] Starting execution for query: {Query}", request.Query);

            // Step 1: Get Schema from Cache (0 LLM calls)
            result.ProcessingSteps.Add("Get schema from cache");
            var schema = await _schemaCache.GetAsync(request.ConnectionId, ct);

            if (schema == null)
            {
                result.Success = false;
                result.ErrorMessage = "Schema not found. Please reconnect to the database.";
                return result;
            }

            // Step 2: Think + Generate SQL - Combined in 1 LLM call
            result.ProcessingSteps.Add("Think + Generate SQL with LLM");
            var (analysis, sql) = await ThinkAndGenerateSqlAsync(request.Query, schema, ct);
            result.SqlGenerated = sql;
            result.LlmCalls = 1;

            _logger.LogInformation("[MediumQueryPipeline] Generated SQL: {Sql}", sql);
            _logger.LogInformation("[MediumQueryPipeline] Analysis: {Analysis}", analysis);

            // Validate SQL before execution
            var validation = ValidateSql(sql);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.ErrorMessage = validation.ErrorMessage;
                return result;
            }

            // Apply LIMIT if not present
            sql = EnsureLimit(sql, request.MaxRows);

            // Step 3: Execute SQL
            var executionResult = await ExecuteWithSelfCorrectionAsync(
                sql, request.Query, schema, analysis, result, ct);

            // If self-correction failed or escalated, return early
            if (result.WasEscalated)
            {
                stopwatch.Stop();
                result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }

            // Step 4: Smart Validation - Check results
            result.ProcessingSteps.Add("Validate result");
            var validationResult = ValidateResult(executionResult, sql);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[MediumQueryPipeline] Result validation failed: {Reason}", validationResult.ErrorMessage);

                // If validation shows clear error and we haven't self-corrected yet
                if (result.LlmCalls <= 2) // 1 for SQL generation + 1 for self-correction
                {
                    // Try self-correction for validation issues
                    var correctionResult = await CorrectSqlAsync(
                        sql,
                        validationResult.ErrorMessage!,
                        schema,
                        request.Query,
                        ct);

                    if (correctionResult.Success && !string.IsNullOrEmpty(correctionResult.CorrectedSql))
                    {
                        sql = EnsureLimit(correctionResult.CorrectedSql, request.MaxRows);
                        executionResult = await _sqlExecutor.ExecuteAsync(sql, ct);
                        result.LlmCalls++;
                        _logger.LogInformation("[MediumQueryPipeline] SQL corrected and re-executed: {Sql}", sql);
                    }
                    else
                    {
                        // Escalate to Complex
                        result.WasEscalated = true;
                        result.EscalationReason = $"Result validation failed after correction: {validationResult.ErrorMessage}";
                        _logger.LogWarning("[MediumQueryPipeline] Escalating to Complex: {Reason}", result.EscalationReason);

                        stopwatch.Stop();
                        result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                        return result;
                    }
                }
            }

            result.QueryResultData = executionResult;

            // Check for escalation triggers
            if (ShouldEscalate(sql, executionResult, request.AmbiguityScore))
            {
                result.WasEscalated = true;
                result.EscalationReason = "Escalation triggers detected: complex subqueries or high ambiguity";
                _logger.LogWarning("[MediumQueryPipeline] Escalating to Complex path");

                stopwatch.Stop();
                result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }

            // Step 5: Format Answer
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
            _logger.LogInformation("[MediumQueryPipeline] Execution completed successfully in {Ms}ms with {Calls} LLM calls",
                stopwatch.ElapsedMilliseconds, result.LlmCalls);

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediumQueryPipeline] Error executing query");

            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.WasEscalated = true;
            result.EscalationReason = $"Exception: {ex.Message}";

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
    }

    private async Task<(string Analysis, string Sql)> ThinkAndGenerateSqlAsync(
        string query,
        DatabaseSchema schema,
        CancellationToken ct)
    {
        var schemaDescription = BuildSchemaDescription(schema);

        var systemPrompt = @"Bạn là chuyên gia SQL. Phân tích câu hỏi và tạo câu SQL phù hợp.

Yêu cầu:
- Chỉ SELECT, không INSERT/UPDATE/DELETE/DROP
- Có thể sử dụng JOIN, GROUP BY, ORDER BY, các hàm aggregation (SUM, COUNT, AVG, MAX, MIN)
- Có thể sử dụng time filters (WHERE date >= ...)
- Có thể sử dụng ranking (TOP, ROW_NUMBER)

Định dạng trả lời:
Analysis: [Phân tích về tables cần dùng, aggregations, filters]
SQL: [Câu SQL query]";

        var userPrompt = $@"Schema: 
{schemaDescription}

Câu hỏi: {query}

Trả lời:";

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

        // Parse response to extract Analysis and SQL
        return ParseThinkSqlResponse(response);
    }

    private (string Analysis, string Sql) ParseThinkSqlResponse(string response)
    {
        var analysis = "";
        var sql = "";

        var lines = response.Split('\n');
        var currentSection = "";

        foreach (var line in lines)
        {
            var upperLine = line.ToUpperInvariant().Trim();

            if (upperLine.StartsWith("ANALYSIS:"))
            {
                currentSection = "analysis";
                analysis = line.Substring(9).Trim();
            }
            else if (upperLine.StartsWith("SQL:"))
            {
                currentSection = "sql";
                sql = line.Substring(4).Trim();
            }
            else if (currentSection == "analysis" && !string.IsNullOrEmpty(line.Trim()))
            {
                analysis += " " + line.Trim();
            }
            else if (currentSection == "sql" && !string.IsNullOrEmpty(line.Trim()))
            {
                sql += " " + line.Trim();
            }
        }

        // If SQL not found in format, try to extract from code blocks
        if (string.IsNullOrEmpty(sql))
        {
            sql = ExtractSql(response);
        }

        return (analysis.Trim(), sql.Trim());
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

    private async Task<SqlExecutionResult> ExecuteWithSelfCorrectionAsync(
        string sql,
        string query,
        DatabaseSchema schema,
        string analysis,
        QueryResult result,
        CancellationToken ct)
    {
        var executionResult = await _sqlExecutor.ExecuteAsync(sql, ct);

        if (!executionResult.Success)
        {
            _logger.LogWarning("[MediumQueryPipeline] SQL execution failed: {Error}", executionResult.ErrorMessage);

            // Try self-correction once
            if (result.LlmCalls < 2) // Only if we haven't already self-corrected
            {
                var correction = await CorrectSqlAsync(
                    sql,
                    executionResult.ErrorMessage!,
                    schema,
                    query,
                    ct);

                if (correction.Success && !string.IsNullOrEmpty(correction.CorrectedSql))
                {
                    _logger.LogInformation("[MediumQueryPipeline] Self-correcting SQL");
                    executionResult = await _sqlExecutor.ExecuteAsync(correction.CorrectedSql, ct);
                    result.LlmCalls++;
                    result.SqlGenerated = correction.CorrectedSql;
                }
                else
                {
                    // Escalate to Complex
                    result.WasEscalated = true;
                    result.EscalationReason = $"SQL execution failed and self-correction failed: {executionResult.ErrorMessage}";
                    _logger.LogWarning("[MediumQueryPipeline] Escalating to Complex after self-correction failure");
                }
            }
            else
            {
                // Escalate to Complex - max self-correction attempts reached
                result.WasEscalated = true;
                result.EscalationReason = $"SQL execution failed after max self-correction attempts: {executionResult.ErrorMessage}";
                _logger.LogWarning("[MediumQueryPipeline] Escalating to Complex - max self-correction attempts reached");
            }
        }

        return executionResult;
    }

    private async Task<CorrectionResult> CorrectSqlAsync(
        string failedSql,
        string errorMessage,
        DatabaseSchema schema,
        string originalQuery,
        CancellationToken ct)
    {
        var schemaDescription = BuildSchemaDescription(schema);

        var systemPrompt = @"Bạn là chuyên gia SQL. Sửa câu SQL bị lỗi dựa trên thông báo lỗi.

Yêu cầu:
- Chỉ trả lời bằng câu SQL đã sửa
- Không giải thích
- Không INSERT/UPDATE/DELETE/DROP";

        var userPrompt = $@"Schema: {schemaDescription}
Câu SQL bị lỗi: {failedSql}
Thông báo lỗi: {errorMessage}
Câu hỏi gốc: {originalQuery}

SQL đã sửa:";

        var correctedSql = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);
        var extractedSql = ExtractSql(correctedSql);

        return new CorrectionResult
        {
            Success = !string.IsNullOrEmpty(extractedSql),
            CorrectedSql = extractedSql
        };
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

        return (true, null);
    }

    private (bool IsValid, string? ErrorMessage) ValidateResult(SqlExecutionResult executionResult, string sql)
    {
        if (!executionResult.Success)
        {
            return (false, executionResult.ErrorMessage);
        }

        // Check if returned 0 rows - might be wrong query
        if (executionResult.Rows == null || executionResult.Rows.Count == 0)
        {
            return (false, "Query returned 0 rows - might need different filters or JOIN conditions");
        }

        // Check for excessive null values in critical columns
        var columnNullCounts = new Dictionary<string, int>();
        foreach (var row in executionResult.Rows)
        {
            foreach (var kvp in row)
            {
                if (kvp.Value == null || kvp.Value == DBNull.Value)
                {
                    if (!columnNullCounts.ContainsKey(kvp.Key))
                        columnNullCounts[kvp.Key] = 0;
                    columnNullCounts[kvp.Key]++;
                }
            }
        }

        // If more than 50% of rows have null in a column, might be a join issue
        var totalRows = executionResult.Rows.Count;
        foreach (var nullCount in columnNullCounts)
        {
            if (nullCount.Value > totalRows * 0.5)
            {
                _logger.LogWarning("[MediumQueryPipeline] Column {Column} has {Pct}% null values",
                    nullCount.Key, (nullCount.Value * 100 / totalRows));
            }
        }

        // Check for unexpected numeric ranges
        var numericColumns = executionResult.Columns
            .Where(c => c.Contains("Amount", StringComparison.OrdinalIgnoreCase) ||
                       c.Contains("Price", StringComparison.OrdinalIgnoreCase) ||
                       c.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                       c.Contains("Qty", StringComparison.OrdinalIgnoreCase) ||
                       c.Contains("Quantity", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var row in executionResult.Rows.Take(10)) // Check first 10 rows
        {
            foreach (var col in numericColumns)
            {
                if (row.TryGetValue(col, out var value) && value != null && value != DBNull.Value)
                {
                    if (value is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.Number)
                        {
                            var numValue = jsonElement.GetDecimal();
                            if (numValue < 0)
                            {
                                return (false, $"Column {col} has negative value which might be incorrect");
                            }
                        }
                    }
                }
            }
        }

        return (true, null);
    }

    private bool ShouldEscalate(string sql, SqlExecutionResult executionResult, double ambiguityScore)
    {
        // ✅ IMPROVED: Smarter escalation rules - less aggressive
        var upperSql = sql.ToUpperInvariant();
        var subqueryCount = CountSubqueries(upperSql);

        // ✅ CHANGED: Allow up to 2 subqueries (was 1)
        if (subqueryCount > 2)
        {
            _logger.LogWarning("[MediumQueryPipeline] Detected {Count} subqueries (>2), escalating to Complex", subqueryCount);
            return true;
        }

        // ✅ CHANGED: Higher ambiguity threshold (0.8 instead of 0.7)
        if (ambiguityScore > 0.8)
        {
            _logger.LogWarning("[MediumQueryPipeline] High ambiguity score {Score} (>0.8), escalating to Complex", ambiguityScore);
            return true;
        }

        // ✅ IMPROVED: Only escalate 0 rows if query has complex filters
        if (executionResult.Rows == null || executionResult.Rows.Count == 0)
        {
            // Check if SQL has complex filters that might need different approach
            if (HasComplexFilters(upperSql))
            {
                _logger.LogWarning("[MediumQueryPipeline] 0 rows with complex filters, escalating to Complex");
                return true;
            }
            else
            {
                // 0 rows but simple query - probably just no matching data, don't escalate
                _logger.LogInformation("[MediumQueryPipeline] 0 rows but simple filters - likely no matching data");
                return false;
            }
        }

        return false;
    }

    private bool HasComplexFilters(string upperSql)
    {
        // Check for complex filter patterns
        var complexPatterns = new[]
        {
            "CASE WHEN",
            "COALESCE",
            "CAST(",
            "CONVERT(",
            "DATEADD",
            "DATEDIFF",
            "SUBSTRING",
            "CHARINDEX"
        };

        return complexPatterns.Any(p => upperSql.Contains(p));
    }

    private int CountSubqueries(string sql)
    {
        // Simple heuristic: count SELECT inside parentheses
        var count = 0;
        var inParen = false;

        for (var i = 0; i < sql.Length - 6; i++)
        {
            if (sql[i] == '(')
            {
                inParen = true;
            }
            else if (inParen && sql.Substring(i, 6).Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
            else if (sql[i] == ')')
            {
                inParen = false;
            }
        }

        return count;
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

    private string BuildSchemaDescription(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        // Add tables
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"Table: {table.TableName}");
            foreach (var col in table.Columns)
            {
                var nullable = col.IsNullable ? "NULL" : "NOT NULL";
                sb.AppendLine($"  - {col.ColumnName} ({col.DataType}) {nullable}");
            }
        }

        // Add relationships
        if (schema.Relationships.Any())
        {
            sb.AppendLine("\nRelationships:");
            foreach (var rel in schema.Relationships)
            {
                sb.AppendLine($"  {rel.FromTable}.{rel.FromColumn} -> {rel.ToTable}.{rel.ToColumn}");
            }
        }

        return sb.ToString();
    }

    private async Task<string> FormatWithLlmAsync(string query, SqlExecutionResult executionResult, CancellationToken ct)
    {
        var systemPrompt = @"Bạn là trợ lý Database. Trả lời bằng tiếng Việt, ngắn gọn, thân thiện.
Đưa ra câu trả lời rõ ràng dựa trên kết quả truy vấn.";

        var dataJson = executionResult.Rows.Count > 0
            ? JsonSerializer.Serialize(executionResult.Rows.Take(10))
            : "No data";

        var userPrompt = $@"Câu hỏi: {query}
Kết quả: {dataJson}
Trả lời:";

        return await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);
    }

    private string FormatWithTemplate(string query, SqlExecutionResult executionResult)
    {
        if (executionResult.Rows == null || executionResult.Rows.Count == 0)
        {
            return "Không có dữ liệu trả về.";
        }

        var rowCount = executionResult.RowCount;

        // Try to extract meaningful info based on query
        var lowerQuery = query.ToLowerInvariant();

        if (lowerQuery.Contains("tổng") || lowerQuery.Contains("sum") || lowerQuery.Contains("total"))
        {
            return $"Tổng cộng có {rowCount} kết quả:";
        }

        if (lowerQuery.Contains("top"))
        {
            return $"Top {Math.Min(rowCount, 10)} kết quả:";
        }

        return $"Tìm thấy {rowCount} kết quả:";
    }
}

/// <summary>
/// Result of SQL correction attempt
/// </summary>
public class CorrectionResult
{
    public bool Success { get; set; }
    public string? CorrectedSql { get; set; }
    public string? ErrorMessage { get; set; }
}
