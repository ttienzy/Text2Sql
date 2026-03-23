using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;

namespace TextToSqlAgent.Application.Pipelines.Write;

/// <summary>
/// WRITE Pipeline - Handles INSERT and UPDATE operations
/// 
/// Flow (10 steps):
/// 0. Validate query relevance (shared)
/// W1. Safety gate - block DELETE/DROP
/// W2. Identify target table
/// W3. Load target table schema
/// W4. RAG - Retrieve table context
/// W5. Generate INSERT/UPDATE SQL
/// W6. Validate WHERE clause (UPDATE only)
/// W7. Preview SQL + user confirm
/// W8. Execute + report affected rows
/// W9. Generate contextual suggestions
/// 
/// Target: <10s execution time, 3 LLM calls
/// ⚠️ MANDATORY: User confirmation required before execution
/// </summary>
public class WritePipeline : IWritePipeline
{
    private readonly ISchemaCache _schemaCache;
    private readonly ILLMClient _llmClient;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ISemanticTableResolver? _semanticResolver;
    private readonly ILogger<WritePipeline> _logger;

    // Hard rule: UPDATE without WHERE is forbidden
    private const string UpdateWithoutWhereError =
        "UPDATE without WHERE clause is forbidden. Please specify which rows to update.";

    // Forbidden keywords that should never appear
    private static readonly string[] ForbiddenKeywords =
    {
        "DROP", "DELETE", "TRUNCATE", "PURGE"
    };

    public WritePipeline(
        ISchemaCache schemaCache,
        ILLMClient llmClient,
        ISqlExecutor sqlExecutor,
        ILogger<WritePipeline> logger,
        ISemanticTableResolver? semanticResolver = null)
    {
        _schemaCache = schemaCache;
        _llmClient = llmClient;
        _sqlExecutor = sqlExecutor;
        _semanticResolver = semanticResolver;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // PREVIEW GENERATION (Steps W1-W7)
    // ═══════════════════════════════════════════════════════════════

    public async Task<WriteOperationPreview> GeneratePreviewAsync(
        WriteOperationRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[WritePipeline] Starting preview generation: {Question}", request.Question);

        try
        {
            // W1: Safety gate - double-check for forbidden operations
            var safetyCheck = PerformSafetyGate(request.Question);
            if (!safetyCheck.IsSafe)
            {
                return new WriteOperationPreview
                {
                    ValidationError = safetyCheck.Error,
                    RequiresConfirmation = false
                };
            }

            // W2: Identify target table with semantic resolution (optimized)
            var schema = await _schemaCache.GetAsync(request.ConnectionId, ct);
            if (schema == null)
            {
                return new WriteOperationPreview
                {
                    ValidationError = "Schema not found. Please reconnect to database.",
                    RequiresConfirmation = false
                };
            }

            string targetTable;
            TableInfo? tableSchema;

            // FIX 1: Use pre-resolved entities from IntentClassifier first (avoid duplicate LLM calls)
            if (request.PreResolvedEntities?.Any() == true)
            {
                _logger.LogInformation("[WritePipeline] Using pre-resolved entities from IntentClassifier: [{Entities}]",
                    string.Join(", ", request.PreResolvedEntities));

                // Try to match pre-resolved entities to actual table names
                var matchedTable = schema.Tables.FirstOrDefault(t =>
                    request.PreResolvedEntities.Any(e =>
                        t.TableName.Equals(e, StringComparison.OrdinalIgnoreCase)));

                if (matchedTable != null)
                {
                    targetTable = matchedTable.TableName;
                    _logger.LogInformation("[WritePipeline] Direct match found: '{Entity}' → '{Table}'",
                        request.PreResolvedEntities.First(), targetTable);
                }
                else
                {
                    // Use semantic resolver for the first entity
                    var primaryEntity = request.PreResolvedEntities.First();
                    var resolution = await _semanticResolver?.ResolveEntityAsync(primaryEntity, schema, ct)
                        ?? new TableResolutionResult { Success = false, ErrorMessage = "Semantic resolver not available" };

                    if (!resolution.Success || resolution.Confidence < 0.7)
                    {
                        if (resolution.IsAmbiguous)
                        {
                            var alternatives = string.Join(", ", resolution.Alternatives.Select(a => a.TableName));
                            return new WriteOperationPreview
                            {
                                ValidationError = $"Ambiguous table reference for '{primaryEntity}'. Did you mean: {alternatives}?",
                                RequiresConfirmation = false
                            };
                        }

                        return new WriteOperationPreview
                        {
                            ValidationError = resolution.ErrorMessage ?? $"Cannot resolve entity '{primaryEntity}' to a table.",
                            RequiresConfirmation = false
                        };
                    }

                    targetTable = resolution.ResolvedTableName!;
                    _logger.LogInformation("[WritePipeline] Semantic resolution: '{Entity}' → '{Table}' (confidence: {Confidence:P0})",
                        primaryEntity, targetTable, resolution.Confidence);
                }
            }
            // Fallback: Use semantic resolution on full question (original behavior)
            else if (_semanticResolver != null)
            {
                _logger.LogInformation("[WritePipeline] No pre-resolved entities, using semantic resolution on full question");

                var resolution = await _semanticResolver.ResolveAsync(request.Question, schema, ct);

                if (!resolution.Success || resolution.Confidence < 0.7)
                {
                    if (resolution.IsAmbiguous)
                    {
                        var alternatives = string.Join(", ", resolution.Alternatives.Select(a => a.TableName));
                        return new WriteOperationPreview
                        {
                            ValidationError = $"Ambiguous table reference. Did you mean: {alternatives}?",
                            RequiresConfirmation = false
                        };
                    }

                    return new WriteOperationPreview
                    {
                        ValidationError = resolution.ErrorMessage ?? "Cannot identify target table. Please specify which table to modify.",
                        RequiresConfirmation = false
                    };
                }

                targetTable = resolution.ResolvedTableName!;
                _logger.LogInformation("[WritePipeline] Semantic resolution: '{Original}' → '{Resolved}' (confidence: {Confidence:P0})",
                    resolution.OriginalMention, targetTable, resolution.Confidence);
            }
            else
            {
                // Last resort: Direct extraction
                _logger.LogInformation("[WritePipeline] Using direct table extraction (no semantic resolver)");
                targetTable = await IdentifyTargetTableAsync(request.Question, ct);

                if (string.IsNullOrEmpty(targetTable))
                {
                    return new WriteOperationPreview
                    {
                        ValidationError = "Cannot identify target table. Please specify which table to modify.",
                        RequiresConfirmation = false
                    };
                }
            }

            // W3: Load target table schema
            tableSchema = schema.Tables.FirstOrDefault(t =>
                t.TableName.Equals(targetTable, StringComparison.OrdinalIgnoreCase));

            if (tableSchema == null)
            {
                return new WriteOperationPreview
                {
                    ValidationError = $"Table '{targetTable}' not found in database schema.",
                    RequiresConfirmation = false
                };
            }

            _logger.LogInformation("[WritePipeline] Target table confirmed: {Table}", targetTable);

            // W4: RAG - Retrieve table context (relationships, constraints)
            var tableContext = BuildTableContext(tableSchema, schema);

            // W5: Generate INSERT/UPDATE SQL
            var (sql, operationType, affectedColumns) = await GenerateSqlAsync(
                request.Question,
                targetTable,
                tableContext,
                ct);

            _logger.LogInformation("[WritePipeline] Generated SQL: {Sql}", sql);

            // W6: Validate WHERE clause for UPDATE
            var validation = ValidateSql(sql, operationType);
            if (!validation.IsValid)
            {
                return new WriteOperationPreview
                {
                    SqlStatement = sql,
                    OperationType = operationType,
                    TargetTable = targetTable,
                    ValidationError = validation.Error,
                    RequiresConfirmation = false
                };
            }

            // W7: Build preview for user confirmation
            var preview = new WriteOperationPreview
            {
                SqlStatement = sql,
                OperationType = operationType,
                TargetTable = targetTable,
                EstimatedAffectedRows = await EstimateAffectedRowsAsync(sql, operationType, ct),
                Warnings = validation.Warnings,
                RequiresConfirmation = true,
                HasWhereClause = validation.HasWhereClause,
                AffectedColumns = affectedColumns
            };

            stopwatch.Stop();
            _logger.LogInformation(
                "[WritePipeline] Preview generated in {Ms}ms: {Type} on {Table}",
                stopwatch.ElapsedMilliseconds, operationType, targetTable);

            return preview;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WritePipeline] Error generating preview");
            return new WriteOperationPreview
            {
                ValidationError = $"Error generating preview: {ex.Message}",
                RequiresConfirmation = false
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EXECUTION (Steps W8-W9)
    // ═══════════════════════════════════════════════════════════════

    public async Task<WriteOperationResult> ExecuteAsync(
        WriteOperationRequest request,
        WriteOperationPreview preview,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var steps = new List<string>();

        _logger.LogInformation(
            "[WritePipeline] Executing write operation: {Type} on {Table}",
            preview.OperationType, preview.TargetTable);

        try
        {
            // Verify confirmation
            if (!request.IsConfirmed)
            {
                return new WriteOperationResult
                {
                    Success = false,
                    ErrorMessage = "User confirmation required before executing write operation",
                    OperationType = preview.OperationType,
                    TargetTable = preview.TargetTable
                };
            }

            // W8: Execute SQL
            steps.Add($"Executing {preview.OperationType} on table {preview.TargetTable}");

            var executionResult = await _sqlExecutor.ExecuteAsync(preview.SqlStatement, ct);

            if (!executionResult.Success)
            {
                return new WriteOperationResult
                {
                    Success = false,
                    ErrorMessage = executionResult.ErrorMessage,
                    SqlExecuted = preview.SqlStatement,
                    OperationType = preview.OperationType,
                    TargetTable = preview.TargetTable,
                    ProcessingSteps = steps
                };
            }

            var affectedRows = executionResult.RowsAffected;
            steps.Add($"Affected {affectedRows} row(s)");

            stopwatch.Stop();

            // W9: Generate contextual suggestions
            var suggestions = await GenerateSuggestionsAsync(
                preview.OperationType,
                preview.TargetTable,
                affectedRows,
                ct);

            _logger.LogInformation(
                "[WritePipeline] Execution complete: {Rows} rows affected in {Ms}ms",
                affectedRows, stopwatch.ElapsedMilliseconds);

            return new WriteOperationResult
            {
                Success = true,
                ActualAffectedRows = affectedRows,
                SqlExecuted = preview.SqlStatement,
                ExecutionTime = stopwatch.Elapsed,
                Suggestions = suggestions,
                OperationType = preview.OperationType,
                TargetTable = preview.TargetTable,
                ProcessingSteps = steps
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WritePipeline] Execution failed");
            return new WriteOperationResult
            {
                Success = false,
                ErrorMessage = $"Execution failed: {ex.Message}",
                SqlExecuted = preview.SqlStatement,
                OperationType = preview.OperationType,
                TargetTable = preview.TargetTable,
                ProcessingSteps = steps
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // W1: SAFETY GATE
    // ═══════════════════════════════════════════════════════════════

    private (bool IsSafe, string? Error) PerformSafetyGate(string question)
    {
        var upper = question.ToUpperInvariant();

        foreach (var keyword in ForbiddenKeywords)
        {
            if (upper.Contains(keyword))
            {
                _logger.LogWarning(
                    "[WritePipeline] Safety gate blocked forbidden keyword: {Keyword}",
                    keyword);

                return (false, $"Forbidden operation detected: {keyword}. This should be handled by FORBIDDEN pipeline.");
            }
        }

        return (true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    // W2: IDENTIFY TARGET TABLE
    // ═══════════════════════════════════════════════════════════════

    private async Task<string> IdentifyTargetTableAsync(string question, CancellationToken ct)
    {
        var prompt = $@"Extract the target table name from this question.
Return ONLY the table name, nothing else.

Question: {question}

Table name:";

        var response = await _llmClient.CompleteAsync(prompt, ct);
        return response.Trim().Trim('"', '\'', '`');
    }

    // ═══════════════════════════════════════════════════════════════
    // W3 & W4: LOAD SCHEMA + BUILD CONTEXT
    // ═══════════════════════════════════════════════════════════════

    private string BuildTableContext(TableInfo table, DatabaseSchema fullSchema)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Table: {table.TableName}");
        sb.AppendLine("Columns:");

        foreach (var col in table.Columns)
        {
            sb.Append($"  - {col.ColumnName} {col.DataType}");
            if (!col.IsNullable) sb.Append(" NOT NULL");
            if (col.IsPrimaryKey) sb.Append(" PRIMARY KEY");
            if (!string.IsNullOrEmpty(col.DefaultValue)) sb.Append($" DEFAULT {col.DefaultValue}");
            sb.AppendLine();
        }

        // Add foreign key relationships
        var relationships = fullSchema.Relationships
            .Where(r => r.FromTable == table.TableName || r.ToTable == table.TableName)
            .ToList();

        if (relationships.Any())
        {
            sb.AppendLine("\nRelationships:");
            foreach (var rel in relationships)
            {
                sb.AppendLine($"  - {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}");
            }
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // W5: GENERATE SQL
    // ═══════════════════════════════════════════════════════════════

    private async Task<(string Sql, WriteOperationType Type, List<string> Columns)> GenerateSqlAsync(
        string question,
        string targetTable,
        string tableContext,
        CancellationToken ct)
    {
        var systemPrompt = @"You are a SQL generator for write operations (INSERT/UPDATE only).

Rules:
1. Generate ONLY INSERT or UPDATE statements
2. For UPDATE: ALWAYS include WHERE clause - never update all rows
3. Use proper data types and quote strings correctly
4. Return valid SQL that can be executed immediately
5. Do NOT include explanations, only SQL

Return JSON format:
{
  ""sql"": ""the SQL statement"",
  ""operation_type"": ""INSERT"" or ""UPDATE"",
  ""affected_columns"": [""column1"", ""column2""]
}";

        var userPrompt = $@"Generate SQL for this request:

Question: {question}

Target table schema:
{tableContext}

Generate the SQL:";

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

        // Parse response
        var cleaned = response.Trim();
        if (cleaned.StartsWith("```json")) cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
        if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
        cleaned = cleaned.Trim();

        var parsed = JsonSerializer.Deserialize<SqlGenerationResponse>(cleaned);

        var operationType = parsed?.OperationType?.ToUpperInvariant() == "INSERT"
            ? WriteOperationType.Insert
            : WriteOperationType.Update;

        return (parsed?.Sql ?? "", operationType, parsed?.AffectedColumns ?? new List<string>());
    }

    private class SqlGenerationResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("sql")]
        public string Sql { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("operation_type")]
        public string OperationType { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("affected_columns")]
        public List<string> AffectedColumns { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════
    // W6: VALIDATE WHERE CLAUSE
    // ═══════════════════════════════════════════════════════════════

    private (bool IsValid, bool HasWhereClause, string? Error, List<string> Warnings) ValidateSql(
        string sql,
        WriteOperationType operationType)
    {
        var upper = sql.ToUpperInvariant();
        var warnings = new List<string>();

        // Check for forbidden keywords
        foreach (var keyword in ForbiddenKeywords)
        {
            if (upper.Contains(keyword))
            {
                return (false, false, $"SQL contains forbidden keyword: {keyword}", warnings);
            }
        }

        // For UPDATE: WHERE clause is MANDATORY
        if (operationType == WriteOperationType.Update)
        {
            var hasWhere = upper.Contains("WHERE");

            if (!hasWhere)
            {
                _logger.LogWarning("[WritePipeline] UPDATE without WHERE clause rejected");
                return (false, false, UpdateWithoutWhereError, warnings);
            }

            // Warn if WHERE clause seems too broad
            if (upper.Contains("WHERE 1=1") || upper.Contains("WHERE TRUE"))
            {
                warnings.Add("WHERE clause appears to match all rows. Please verify this is intentional.");
            }

            return (true, true, null, warnings);
        }

        // For INSERT: basic validation
        if (operationType == WriteOperationType.Insert)
        {
            if (!upper.Contains("INSERT INTO"))
            {
                return (false, false, "Invalid INSERT statement", warnings);
            }

            return (true, false, null, warnings);
        }

        return (false, false, "Unknown operation type", warnings);
    }

    // ═══════════════════════════════════════════════════════════════
    // W7: ESTIMATE AFFECTED ROWS
    // ═══════════════════════════════════════════════════════════════

    private async Task<int> EstimateAffectedRowsAsync(
        string sql,
        WriteOperationType operationType,
        CancellationToken ct)
    {
        try
        {
            if (operationType == WriteOperationType.Insert)
            {
                // INSERT affects 1 row (or count of VALUES)
                return 1;
            }

            // For UPDATE: try to estimate by converting to SELECT COUNT
            var estimateQuery = ConvertToCountQuery(sql);
            if (string.IsNullOrEmpty(estimateQuery))
            {
                return -1; // Unknown
            }

            var result = await _sqlExecutor.ExecuteAsync(estimateQuery, ct);
            if (result.Success && result.Rows?.Count > 0)
            {
                var countValue = result.Rows[0].Values.FirstOrDefault();
                if (int.TryParse(countValue?.ToString(), out var count))
                {
                    return count;
                }
            }

            return -1; // Unknown
        }
        catch
        {
            return -1; // Unknown
        }
    }

    private string ConvertToCountQuery(string updateSql)
    {
        try
        {
            // Extract table and WHERE clause from UPDATE statement
            // UPDATE table SET ... WHERE condition
            var upper = updateSql.ToUpperInvariant();
            var updateIndex = upper.IndexOf("UPDATE");
            var setIndex = upper.IndexOf("SET");
            var whereIndex = upper.IndexOf("WHERE");

            if (updateIndex == -1 || setIndex == -1)
                return string.Empty;

            var tableName = updateSql.Substring(updateIndex + 6, setIndex - updateIndex - 6).Trim();

            if (whereIndex > 0)
            {
                var whereClause = updateSql.Substring(whereIndex);
                return $"SELECT COUNT(*) FROM {tableName} {whereClause}";
            }

            return $"SELECT COUNT(*) FROM {tableName}";
        }
        catch
        {
            return string.Empty;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // W9: GENERATE SUGGESTIONS
    // ═══════════════════════════════════════════════════════════════

    private async Task<List<string>> GenerateSuggestionsAsync(
        WriteOperationType operationType,
        string targetTable,
        int affectedRows,
        CancellationToken ct)
    {
        var suggestions = new List<string>();

        // Always suggest verification
        suggestions.Add($"Verify the changes: SELECT * FROM {targetTable} ORDER BY id DESC LIMIT 10");

        if (operationType == WriteOperationType.Insert)
        {
            suggestions.Add($"Check total count: SELECT COUNT(*) FROM {targetTable}");
        }
        else if (operationType == WriteOperationType.Update)
        {
            suggestions.Add($"Review updated records to ensure correctness");

            if (affectedRows > 10)
            {
                suggestions.Add($"⚠️ {affectedRows} rows were updated. Consider reviewing all affected records.");
            }
        }

        return suggestions;
    }
}
