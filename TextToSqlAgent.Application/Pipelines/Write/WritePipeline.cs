using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;

namespace TextToSqlAgent.Application.Pipelines.Write;

/// <summary>
/// DML Pipeline - Handles INSERT, UPDATE, DELETE (with WHERE), and UPSERT operations
/// 
/// Flow (10 steps):
/// 0. Validate query relevance (shared)
/// W1. Safety gate - block DROP/TRUNCATE (DELETE allowed if routed by IntentClassifier)
/// W2. Identify target table
/// W3. Load target table schema
/// W4. RAG - Retrieve MULTI-TABLE context (primary + top 4 related tables)
/// W5. Generate INSERT/UPDATE/DELETE/UPSERT SQL
/// W6. Validate WHERE clause (UPDATE/DELETE mandatory)
/// W7. Preview SQL + user confirm (via SSE awaiting_confirm event)
/// W8. Execute + report affected rows
/// W9. Generate contextual suggestions
/// 
/// Target: <10s execution time, 3 LLM calls
/// ⚠️ MANDATORY: User confirmation required before execution
/// ✅ REFACTORED: Multi-table context prevents FK hallucination
/// ✅ REFACTORED: DELETE with WHERE now routable (risk=CRITICAL, confirm required)
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

    // Forbidden keywords that should never appear in DML SQL
    // ✅ REFACTORED: DELETE removed — now allowed with WHERE clause via IntentClassifier routing
    private static readonly string[] ForbiddenKeywords =
    {
        "DROP", "TRUNCATE", "PURGE"
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
            // ✅ OPTIMIZATION: Use injected schema first to avoid Redis round-trip
            var schema = request.Schema ?? await _schemaCache.GetAsync(request.ConnectionId, ct);
            if (schema == null)
            {
                return new WriteOperationPreview
                {
                    ValidationError = "Schema not found. Please reconnect to database.",
                    RequiresConfirmation = false
                };
            }

            _logger.LogDebug("[WritePipeline] Schema source: {Source}",
                request.Schema != null ? "Injected" : "Cache");

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
    // W2: IDENTIFY TARGET TABLE (REMOVED - Use IntentClassifier entities)
    // ═══════════════════════════════════════════════════════════════
    // ✅ OPTIMIZATION: This method is now deprecated. 
    // Table identification is handled by IntentClassifier.ClassifyAsync()
    // which provides PreResolvedEntities in the request.
    // Keeping this as fallback only for legacy code paths.

    private async Task<string> IdentifyTargetTableAsync(string question, CancellationToken ct)
    {
        _logger.LogWarning("[WritePipeline] ⚠️ Using fallback table identification - IntentClassifier should provide entities");

        var prompt = $@"Extract table name from: {question}
Return ONLY the table name:";

        var response = await _llmClient.CompleteAsync(prompt, ct);
        return response.Trim().Trim('"', '\'', '`');
    }

    /// <summary>
    /// Build multi-table context: primary table + top N related tables via FK relationships.
    /// ✅ REFACTORED: Replaces single-table context to prevent FK hallucination.
    /// The LLM now sees the full relationship graph needed for correct SQL generation.
    /// </summary>
    private string BuildTableContext(TableInfo primaryTable, DatabaseSchema fullSchema)
    {
        var sb = new StringBuilder();

        // === PRIMARY TABLE (full detail) ===
        sb.AppendLine($"═══ PRIMARY TARGET TABLE ═══");
        sb.AppendLine($"Table: {primaryTable.TableName}");
        sb.AppendLine("Columns:");

        foreach (var col in primaryTable.Columns)
        {
            sb.Append($"  - {col.ColumnName} {col.DataType}");
            if (!col.IsNullable) sb.Append(" NOT NULL");
            if (col.IsPrimaryKey) sb.Append(" PRIMARY KEY");
            if (col.IsForeignKey) sb.Append(" FK");
            if (!string.IsNullOrEmpty(col.DefaultValue)) sb.Append($" DEFAULT {col.DefaultValue}");
            sb.AppendLine();
        }

        // === FOREIGN KEY RELATIONSHIPS ===
        var relationships = fullSchema.Relationships
            .Where(r => r.FromTable.Equals(primaryTable.TableName, StringComparison.OrdinalIgnoreCase)
                     || r.ToTable.Equals(primaryTable.TableName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relationships.Any())
        {
            sb.AppendLine("\nForeign Key Relationships:");
            foreach (var rel in relationships)
            {
                sb.AppendLine($"  - {rel.FromTable}.{rel.FromColumn} → {rel.ToTable}.{rel.ToColumn}");
            }
        }

        // === RELATED TABLES (top 4, summary only) ===
        var relatedTableNames = relationships
            .SelectMany(r => new[] { r.FromTable, r.ToTable })
            .Where(t => !t.Equals(primaryTable.TableName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (relatedTableNames.Any())
        {
            sb.AppendLine("\n═══ RELATED TABLES (for FK lookup) ═══");

            foreach (var relTableName in relatedTableNames)
            {
                var relTable = fullSchema.Tables.FirstOrDefault(t =>
                    t.TableName.Equals(relTableName, StringComparison.OrdinalIgnoreCase));

                if (relTable == null) continue;

                sb.AppendLine($"\nTable: {relTable.TableName}");
                sb.AppendLine("Key Columns:");

                // Show PK + FK + frequently-referenced columns only (compact)
                var keyColumns = relTable.Columns
                    .Where(c => c.IsPrimaryKey || c.IsForeignKey
                        || c.ColumnName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                        || c.ColumnName.EndsWith("Name", StringComparison.OrdinalIgnoreCase)
                        || c.ColumnName.EndsWith("Code", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // If no key columns detected, show first 5 columns
                if (!keyColumns.Any())
                {
                    keyColumns = relTable.Columns.Take(5).ToList();
                }

                foreach (var col in keyColumns)
                {
                    sb.Append($"  - {col.ColumnName} {col.DataType}");
                    if (col.IsPrimaryKey) sb.Append(" PK");
                    if (col.IsForeignKey) sb.Append(" FK");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // W5: GENERATE SQL (OPTIMIZED - Single LLM call)
    // ═══════════════════════════════════════════════════════════════

    private async Task<(string Sql, WriteOperationType Type, List<string> Columns)> GenerateSqlAsync(
        string question,
        string targetTable,
        string tableContext,
        CancellationToken ct)
    {
        // ✅ OPTIMIZATION: Compact system prompt to reduce token usage
        var systemPrompt = @"Generate SQL for DML operations (INSERT/UPDATE/DELETE/UPSERT).

Rules:
1. UPDATE/DELETE: ALWAYS include WHERE clause
2. Use proper data types, quote strings with N'' for Unicode
3. Use ONLY columns from provided schema
4. Return valid SQL Server syntax
5. CRITICAL: Return ONLY valid JSON, no markdown, no explanations

JSON format (STRICT):
{""sql"":""statement"",""operation_type"":""INSERT|UPDATE|DELETE|UPSERT"",""affected_columns"":[""col1""]}";

        var userPrompt = $@"Question: {question}

Schema:
{tableContext}

Generate SQL (JSON only):";

        var maxRetries = 3;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

                // ✅ DEFENSIVE PARSING: Multiple strategies
                var parsed = TryParseJsonResponse(response, attempt);

                if (parsed != null)
                {
                    var operationType = parsed.OperationType?.ToUpperInvariant() switch
                    {
                        "INSERT" => WriteOperationType.Insert,
                        "UPDATE" => WriteOperationType.Update,
                        "DELETE" => WriteOperationType.Delete,
                        "UPSERT" or "MERGE" => WriteOperationType.Upsert,
                        _ => WriteOperationType.Update
                    };

                    return (parsed.Sql ?? "", operationType, parsed.AffectedColumns ?? new List<string>());
                }

                // If parsing failed but no exception, retry with stronger prompt
                if (attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "[WritePipeline] Attempt {Attempt}/{MaxRetries} - LLM returned non-JSON response, retrying with stricter prompt",
                        attempt, maxRetries);

                    // Re-prompt with error feedback
                    systemPrompt = @"CRITICAL: You MUST return ONLY valid JSON. No markdown, no explanations, no text before or after.

Previous attempt failed because you returned text instead of JSON.

JSON format (STRICT):
{""sql"":""statement"",""operation_type"":""INSERT|UPDATE|DELETE|UPSERT"",""affected_columns"":[""col1""]}

Example valid response:
{""sql"":""INSERT INTO Customers (Name) VALUES ('John')"",""operation_type"":""INSERT"",""affected_columns"":[""Name""]}";

                    await Task.Delay(500 * attempt, ct); // Exponential backoff
                    continue;
                }
            }
            catch (JsonException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "[WritePipeline] Attempt {Attempt}/{MaxRetries} - JSON parsing failed",
                    attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    await Task.Delay(500 * attempt, ct);
                    continue;
                }
            }
        }

        // All retries failed - return error with helpful message
        _logger.LogError(lastException,
            "[WritePipeline] Failed to parse LLM response after {MaxRetries} attempts. Question: {Question}",
            maxRetries, question);

        throw new InvalidOperationException(
            $"LLM failed to generate valid SQL JSON after {maxRetries} attempts. " +
            $"This may indicate the question requires DDL operations (ALTER TABLE, CREATE TABLE) " +
            $"which should be handled by DDL pipeline, not Write pipeline. " +
            $"Question: {question}",
            lastException);
    }

    /// <summary>
    /// Defensive JSON parsing with multiple fallback strategies
    /// Based on best practices from production LLM applications
    /// </summary>
    private SqlGenerationResponse? TryParseJsonResponse(string response, int attempt)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("[WritePipeline] LLM returned empty response");
            return null;
        }

        // Strategy 1: Clean markdown wrappers
        var cleaned = response.Trim();
        if (cleaned.StartsWith("```json"))
        {
            cleaned = cleaned[7..];
            _logger.LogDebug("[WritePipeline] Stripped ```json prefix");
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned[3..];
            _logger.LogDebug("[WritePipeline] Stripped ``` prefix");
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned[..^3];
            _logger.LogDebug("[WritePipeline] Stripped ``` suffix");
        }

        cleaned = cleaned.Trim();

        // Strategy 2: Try direct parse
        try
        {
            var parsed = JsonSerializer.Deserialize<SqlGenerationResponse>(cleaned);
            if (parsed != null && !string.IsNullOrEmpty(parsed.Sql))
            {
                _logger.LogDebug("[WritePipeline] Successfully parsed JSON (direct)");
                return parsed;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "[WritePipeline] Direct JSON parse failed, trying fallback strategies");
        }

        // Strategy 3: Extract JSON from text (find first { to last })
        var firstBrace = cleaned.IndexOf('{');
        var lastBrace = cleaned.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var extracted = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            try
            {
                var parsed = JsonSerializer.Deserialize<SqlGenerationResponse>(extracted);
                if (parsed != null && !string.IsNullOrEmpty(parsed.Sql))
                {
                    _logger.LogDebug("[WritePipeline] Successfully parsed JSON (extracted from text)");
                    return parsed;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "[WritePipeline] Extracted JSON parse failed");
            }
        }

        // Strategy 4: Check if response is plain text explanation (not JSON at all)
        if (!cleaned.Contains('{') || !cleaned.Contains('}'))
        {
            _logger.LogWarning(
                "[WritePipeline] LLM returned plain text instead of JSON. Response preview: {Preview}",
                cleaned.Length > 100 ? cleaned[..100] + "..." : cleaned);
            return null;
        }

        // All strategies failed
        _logger.LogWarning(
            "[WritePipeline] All parsing strategies failed. Response preview: {Preview}",
            cleaned.Length > 200 ? cleaned[..200] + "..." : cleaned);

        return null;
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

        // Check for forbidden keywords (DROP, TRUNCATE, PURGE)
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

        // ✅ NEW: For DELETE: WHERE clause is MANDATORY (critical safety)
        if (operationType == WriteOperationType.Delete)
        {
            var hasWhere = upper.Contains("WHERE");

            if (!hasWhere)
            {
                _logger.LogWarning("[WritePipeline] DELETE without WHERE clause rejected — this is a mass deletion");
                return (false, false, "DELETE without WHERE clause is forbidden. This would delete ALL rows.", warnings);
            }

            // Extra safety for DELETE: always warn
            warnings.Add("⚠️ DELETE operation — rows will be permanently removed. This action cannot be undone.");

            if (upper.Contains("WHERE 1=1") || upper.Contains("WHERE TRUE"))
            {
                return (false, false, "DELETE with WHERE 1=1 is equivalent to DELETE ALL — forbidden.", warnings);
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

        // ✅ NEW: For UPSERT/MERGE: basic validation
        if (operationType == WriteOperationType.Upsert)
        {
            if (!upper.Contains("MERGE") && !upper.Contains("INSERT"))
            {
                return (false, false, "Invalid UPSERT/MERGE statement", warnings);
            }

            warnings.Add("UPSERT operation — will insert new rows or update existing ones.");
            return (true, upper.Contains("WHEN MATCHED"), null, warnings);
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

            // For UPDATE/DELETE: try to estimate by converting to SELECT COUNT
            var estimateQuery = ConvertToCountQuery(sql, operationType);
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

    private string ConvertToCountQuery(string dmlSql, WriteOperationType operationType)
    {
        try
        {
            var upper = dmlSql.ToUpperInvariant();
            string tableName;
            int whereIndex;

            if (operationType == WriteOperationType.Delete)
            {
                // DELETE FROM table WHERE condition
                var fromIndex = upper.IndexOf("FROM");
                whereIndex = upper.IndexOf("WHERE");

                if (fromIndex == -1) return string.Empty;

                var afterFrom = fromIndex + 4;
                var endOfTable = whereIndex > 0 ? whereIndex : dmlSql.Length;
                tableName = dmlSql.Substring(afterFrom, endOfTable - afterFrom).Trim();
            }
            else
            {
                // UPDATE table SET ... WHERE condition
                var updateIndex = upper.IndexOf("UPDATE");
                var setIndex = upper.IndexOf("SET");
                whereIndex = upper.IndexOf("WHERE");

                if (updateIndex == -1 || setIndex == -1)
                    return string.Empty;

                tableName = dmlSql.Substring(updateIndex + 6, setIndex - updateIndex - 6).Trim();
            }

            if (whereIndex > 0)
            {
                var whereClause = dmlSql.Substring(whereIndex);
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
        suggestions.Add($"Verify the changes: SELECT * FROM {targetTable} ORDER BY 1 DESC");

        switch (operationType)
        {
            case WriteOperationType.Insert:
                suggestions.Add($"Check total count: SELECT COUNT(*) FROM {targetTable}");
                break;

            case WriteOperationType.Update:
                suggestions.Add($"Review updated records to ensure correctness");
                if (affectedRows > 10)
                    suggestions.Add($"⚠️ {affectedRows} rows were updated. Consider reviewing all affected records.");
                break;

            case WriteOperationType.Delete:
                suggestions.Add($"Check remaining count: SELECT COUNT(*) FROM {targetTable}");
                if (affectedRows > 1)
                    suggestions.Add($"⚠️ {affectedRows} rows were permanently deleted.");
                break;

            case WriteOperationType.Upsert:
                suggestions.Add($"Review affected records: SELECT TOP 10 * FROM {targetTable} ORDER BY 1 DESC");
                break;
        }

        return suggestions;
    }
}
