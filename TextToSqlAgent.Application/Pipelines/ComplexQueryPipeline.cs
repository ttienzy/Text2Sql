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
/// Complex Query Pipeline - handles 5% of queries (subqueries, trend analysis, comparisons)
/// Target: 30-60 seconds with ReAct agent
/// 
/// Flow:
/// 1. Get Schema from Cache (0 LLM calls)
/// 2. Think Phase - analyze query deeply (1 LLM call)
/// 3. Generate SQL with subqueries (1 LLM call)
/// 4. Execute SQL
/// 5. Validate Result - complex validation
/// 6. Self-Correction - max 2 times
/// 7. Format Answer - LLM-based
/// </summary>
public class ComplexQueryPipeline : IComplexQueryPipeline
{
    private readonly ISchemaCache _schemaCache;
    private readonly ILLMClient _llmClient;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ILogger<ComplexQueryPipeline> _logger;

    // SQL validation - dangerous keywords
    private static readonly string[] ForbiddenKeywords =
    {
        "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE", "EXEC", "EXECUTE"
    };

    // Maximum self-correction attempts
    private const int MaxSelfCorrectionAttempts = 2;

    public ComplexQueryPipeline(
        ISchemaCache schemaCache,
        ILLMClient llmClient,
        ISqlExecutor sqlExecutor,
        ILogger<ComplexQueryPipeline> logger)
    {
        _schemaCache = schemaCache;
        _llmClient = llmClient;
        _sqlExecutor = sqlExecutor;
        _logger = logger;
    }

    public bool CanHandle(QueryComplexity complexity)
    {
        return complexity == QueryComplexity.Complex;
    }

    public async Task<QueryResult> ExecuteAsync(ComplexQueryRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new QueryResult
        {
            Complexity = QueryComplexity.Complex,
            ProcessingSteps = new List<string>()
        };

        try
        {
            _logger.LogInformation("[ComplexPipeline] Starting complex query execution: {Query}", request.Query);
            result.ProcessingSteps.Add("Loading schema from cache");

            // 1. Get schema from cache
            var schema = await _schemaCache.GetOrSetAsync(
                request.ConnectionId,
                () => throw new InvalidOperationException("Schema not available. Please scan database first."),
                ct);

            result.ProcessingSteps.Add($"Schema loaded: {schema.Tables.Count} tables");

            // 2. Think phase - analyze query deeply
            result.ProcessingSteps.Add("Analyzing query complexity (think phase)");
            var thinkingResult = await ThinkPhaseAsync(request.Query, schema, ct);
            result.LlmCalls++;

            if (!string.IsNullOrEmpty(thinkingResult.Error))
            {
                return CreateErrorResult(result, thinkingResult.Error, stopwatch);
            }

            result.ProcessingSteps.Add("Think phase completed");

            // 3. Generate SQL with potential subqueries
            result.ProcessingSteps.Add("Generating SQL with subqueries");
            var sqlResult = await GenerateSqlWithSubqueriesAsync(request.Query, schema, thinkingResult.Analysis, ct);
            result.LlmCalls++;

            if (!string.IsNullOrEmpty(sqlResult.Error))
            {
                return CreateErrorResult(result, sqlResult.Error, stopwatch);
            }

            var sql = sqlResult.Sql ?? "";
            result.ProcessingSteps.Add($"SQL generated: {sql}");

            // Validate SQL
            if (!IsSqlValid(sql, result))
            {
                return result;
            }

            // 4. Execute SQL
            result.ProcessingSteps.Add("Executing SQL");
            var executionResult = await ExecuteSqlAsync(sql, ct);

            if (!executionResult.Success)
            {
                // 5. Self-correction if failed
                result.ProcessingSteps.Add("SQL execution failed, attempting self-correction");
                var corrected = await SelfCorrectAsync(
                    request.Query, schema, sql, executionResult.ErrorMessage ?? "Unknown error",
                    ct, result);

                if (corrected != null)
                {
                    result.LlmCalls++;
                    result.ProcessingSteps.Add("Self-correction applied");
                }
                else
                {
                    return CreateErrorResult(result, executionResult.ErrorMessage ?? "SQL execution failed", stopwatch);
                }
            }
            else
            {
                result.QueryResultData = executionResult;
                result.ProcessingSteps.Add($"Query executed successfully: {executionResult.Rows.Count} rows");
            }

            // 6. Validate result
            result.ProcessingSteps.Add("Validating results");
            if (!ValidateResult(result.QueryResultData, result))
            {
                _logger.LogWarning("[ComplexPipeline] Result validation failed");
            }

            // 7. Format answer with LLM
            if (request.UseLlmFormatting)
            {
                result.ProcessingSteps.Add("Formatting answer with LLM");
                var formatted = await FormatAnswerAsync(request.Query, result.QueryResultData, schema, ct);
                result.LlmCalls++;
                result.FormattedAnswer = formatted;
            }
            else
            {
                result.FormattedAnswer = FormatResultTemplate(result.QueryResultData);
            }

            result.Success = true;
            result.ProcessingSteps.Add("Query completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ComplexPipeline] Error executing query");
            return CreateErrorResult(result, ex.Message, stopwatch);
        }

        stopwatch.Stop();
        result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private async Task<ThinkingResult> ThinkPhaseAsync(string query, DatabaseSchema schema, CancellationToken ct)
    {
        var prompt = $@"Analyze this complex database query:
Query: {query}

Database Schema:
{FormatSchemaForPrompt(schema)}

Analyze:
1. What tables are needed?
2. Are subqueries required?
3. What aggregations are needed?
4. Are there any ambiguous terms that need interpretation?

Provide your analysis in JSON format:
{{
    ""tables"": [""table1"", ""table2""],
    ""subqueries_needed"": true,
    ""aggregations"": [""SUM"", ""COUNT""],
    ""analysis"": ""detailed explanation""
}}";

        var response = await _llmClient.CompleteWithSystemPromptAsync(
            "You are a database expert. Respond with JSON only.",
            $"Analyze: {prompt}",
            ct);

        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                return new ThinkingResult
                {
                    Analysis = data?.TryGetValue("analysis", out var a) == true ? a.GetString() ?? "" : "",
                    Tables = data?.TryGetValue("tables", out var t) == true
                        ? t.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                        : new List<string>(),
                    SubqueriesNeeded = data?.TryGetValue("subqueries_needed", out var s) == true && s.GetBoolean()
                };
            }
        }
        catch
        {
            // Fallback
        }

        return new ThinkingResult { Analysis = response };
    }

    private async Task<SqlGenerationResult> GenerateSqlWithSubqueriesAsync(
        string query, DatabaseSchema schema, string analysis, CancellationToken ct)
    {
        var systemPrompt = @"Bạn là chuyên gia SQL. Chỉ trả lời bằng câu SQL, không giải thích.
Có thể dùng subqueries, JOINs, GROUP BY, window functions nếu cần.
Chỉ SELECT, không INSERT/UPDATE/DELETE/DROP.";

        var userPrompt = $@"Previous Analysis: {analysis}

Query: {query}

Database Schema:
{FormatSchemaForPrompt(schema)}

SQL:";

        var sql = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

        // Clean up the SQL
        sql = CleanSql(sql);

        return new SqlGenerationResult { Sql = sql };
    }

    private async Task<SqlExecutionResult> ExecuteSqlAsync(string sql, CancellationToken ct)
    {
        return await _sqlExecutor.ExecuteAsync(sql, ct);
    }

    private async Task<string?> SelfCorrectAsync(
        string query, DatabaseSchema schema, string originalSql, string error,
        CancellationToken ct, QueryResult result)
    {
        for (int i = 0; i < MaxSelfCorrectionAttempts; i++)
        {
            result.ProcessingSteps.Add($"Self-correction attempt {i + 1}");

            var systemPrompt = "You are a SQL expert. Fix the error and return only the SQL query.";
            var userPrompt = $@"Fix this SQL error.

Original Query: {query}
Error: {error}

Database Schema:
{FormatSchemaForPrompt(schema)}

Invalid SQL:
{originalSql}

Fixed SQL:";

            var correctedSql = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

            correctedSql = CleanSql(correctedSql);

            if (!IsSqlValid(correctedSql, result))
            {
                continue;
            }

            var execResult = await _sqlExecutor.ExecuteAsync(correctedSql, ct);

            if (execResult.Success)
            {
                result.SqlGenerated = correctedSql;
                result.QueryResultData = execResult;
                result.ProcessingSteps.Add($"Self-correction successful on attempt {i + 1}");
                return correctedSql;
            }
        }

        return null;
    }

    private bool ValidateResult(SqlExecutionResult? data, QueryResult result)
    {
        if (data == null || data.Rows == null || data.Rows.Count == 0)
        {
            result.FormattedAnswer = "Query executed successfully but returned no results.";
            return true;
        }

        // Check for excessive nulls
        var nullCount = 0;
        var totalCells = 0;
        if (data.Rows != null)
        {
            foreach (var row in data.Rows)
            {
                if (row != null)
                {
                    foreach (var cell in row)
                    {
                        totalCells++;
                        if (cell.Value == null || cell.Value == DBNull.Value)
                            nullCount++;
                    }
                }
            }
        }

        return totalCells == 0 || nullCount < totalCells * 0.5;
    }

    private async Task<string> FormatAnswerAsync(
        string query, SqlExecutionResult? data, DatabaseSchema schema, CancellationToken ct)
    {
        var systemPrompt = "Bạn là trợ lý DB. Trả lời bằng tiếng Việt, ngắn gọn và rõ ràng.";
        var userPrompt = $@"Format this query result as natural language.

Query: {query}

Result Data:
{FormatDataForLLM(data)}

Answer:";

        return await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);
    }

    private string FormatResultTemplate(SqlExecutionResult? data)
    {
        if (data?.Rows == null || data.Rows.Count == 0)
            return "No results found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {data.Rows.Count} results:");

        var headers = data.Columns ?? new List<string>();
        foreach (var row in data.Rows.Take(10))
        {
            if (row == null) continue;
            var cells = headers.Zip(row, (h, kv) => $"{h}: {kv.Value}");
            sb.AppendLine("- " + string.Join(", ", cells));
        }

        if (data.Rows.Count > 10)
            sb.AppendLine($"... and {data.Rows.Count - 10} more rows");

        return sb.ToString();
    }

    private bool IsSqlValid(string sql, QueryResult result)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            result.Success = false;
            result.ErrorMessage = "No SQL generated";
            return false;
        }

        var upperSql = sql.ToUpperInvariant();
        foreach (var keyword in ForbiddenKeywords)
        {
            if (upperSql.Contains(keyword))
            {
                result.Success = false;
                result.ErrorMessage = $"SQL contains forbidden keyword: {keyword}";
                result.ProcessingSteps.Add($"Security: Blocked forbidden keyword {keyword}");
                return false;
            }
        }

        result.SqlGenerated = sql;
        return true;
    }

    private QueryResult CreateErrorResult(QueryResult result, string error, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        result.Success = false;
        result.ErrorMessage = error;
        result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private string FormatSchemaForPrompt(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"Table: {table.TableName}");
            foreach (var col in table.Columns)
            {
                sb.AppendLine($"  - {col.ColumnName}: {col.DataType} {(col.IsPrimaryKey ? "(PK)" : "")}");
            }
        }
        return sb.ToString();
    }

    private string FormatDataForLLM(SqlExecutionResult? data)
    {
        if (data?.Rows == null) return "No data";

        var sb = new StringBuilder();
        var headers = data.Columns ?? new List<string>();

        foreach (var row in data.Rows.Take(20))
        {
            if (row == null) continue;
            var cells = headers.Zip(row, (h, kv) => $"{h}={kv.Value}");
            sb.AppendLine(string.Join(", ", cells));
        }

        return sb.ToString();
    }

    private string CleanSql(string sql)
    {
        // Remove markdown code blocks
        sql = sql.Replace("```sql", "").Replace("```", "").Trim();

        // Remove common prefixes
        var lines = sql.Split('\n')
            .Where(l => !l.Trim().StartsWith("SQL:", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || l.Trim().StartsWith("SELECT"));

        return string.Join("\n", lines).Trim();
    }

    private class ThinkingResult
    {
        public string Analysis { get; set; } = "";
        public List<string> Tables { get; set; } = new();
        public bool SubqueriesNeeded { get; set; }
        public string? Error { get; set; }
    }

    private class SqlGenerationResult
    {
        public string? Sql { get; set; }
        public string? Error { get; set; }
    }
}
