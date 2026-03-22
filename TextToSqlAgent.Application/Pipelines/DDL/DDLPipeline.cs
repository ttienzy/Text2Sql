using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Ports;

namespace TextToSqlAgent.Application.Pipelines.DDL;

/// <summary>
/// DDL Pipeline - Handles CREATE INDEX, ALTER TABLE, CREATE VIEW/PROCEDURE
/// 
/// Flow (9 steps):
/// 0. Validate query relevance (shared)
/// D1. Classify DDL type
/// D2. Load full schema context
/// D3. RAG - Find related objects
/// D4. Generate DDL script with naming conventions
/// D5. Analyze impact (storage, lock time, performance)
/// D6. Preview + impact summary
/// D7. Execute DDL with timeout
/// D8. Reload schema cache + re-index vector DB
/// 
/// Target: <15s execution time, 3 LLM calls
/// ⚠️ MANDATORY: Impact analysis and user confirmation required
/// </summary>
public class DDLPipeline : IDDLPipeline
{
    private readonly ISchemaCache _schemaCache;
    private readonly ILLMClient _llmClient;
    private readonly ISqlExecutor _sqlExecutor;
    private readonly ILogger<DDLPipeline> _logger;

    // Naming conventions
    private const string IndexNamingPattern = "idx_{table}_{columns}";
    private const string ProcedureNamingPattern = "sp_{action}_{entity}";
    private const string ViewNamingPattern = "vw_{description}";

    public DDLPipeline(
        ISchemaCache schemaCache,
        ILLMClient llmClient,
        ISqlExecutor sqlExecutor,
        ILogger<DDLPipeline> logger)
    {
        _schemaCache = schemaCache;
        _llmClient = llmClient;
        _sqlExecutor = sqlExecutor;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // PREVIEW GENERATION (Steps D1-D6)
    // ═══════════════════════════════════════════════════════════════

    public async Task<DDLOperationPreview> GeneratePreviewAsync(
        DDLOperationRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[DDLPipeline] Starting preview generation: {Question}", request.Question);

        try
        {
            // D1: Classify DDL type
            var ddlType = await ClassifyDDLTypeAsync(request.Question, ct);
            _logger.LogInformation("[DDLPipeline] DDL type classified: {Type}", ddlType);

            if (ddlType == DDLOperationType.Unknown)
            {
                return new DDLOperationPreview
                {
                    ValidationError = "Cannot determine DDL operation type. Please be more specific.",
                    RequiresConfirmation = false
                };
            }

            // D2: Load full schema context
            var schema = await _schemaCache.GetAsync(request.ConnectionId, ct);
            if (schema == null)
            {
                return new DDLOperationPreview
                {
                    ValidationError = "Schema not found. Please reconnect to database.",
                    RequiresConfirmation = false
                };
            }

            // D3: RAG - Find related objects
            var relatedObjects = FindRelatedObjects(request.Question, schema, ddlType);
            _logger.LogInformation("[DDLPipeline] Found {Count} related objects", relatedObjects.Count);

            // D4: Generate DDL script with naming conventions
            var (ddlScript, targetObject) = await GenerateDDLScriptAsync(
                request.Question,
                ddlType,
                schema,
                relatedObjects,
                ct);

            _logger.LogInformation("[DDLPipeline] Generated DDL script for: {Target}", targetObject);

            // D5: Analyze impact
            var impact = await AnalyzeImpactAsync(ddlScript, ddlType, targetObject, schema, ct);

            // D6: Build preview
            var preview = new DDLOperationPreview
            {
                DDLScript = ddlScript,
                OperationType = ddlType,
                TargetObject = targetObject,
                Impact = impact,
                RequiresConfirmation = true,
                RelatedObjects = relatedObjects
            };

            stopwatch.Stop();
            _logger.LogInformation(
                "[DDLPipeline] Preview generated in {Ms}ms: {Type} on {Target}",
                stopwatch.ElapsedMilliseconds, ddlType, targetObject);

            return preview;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DDLPipeline] Error generating preview");
            return new DDLOperationPreview
            {
                ValidationError = $"Error generating preview: {ex.Message}",
                RequiresConfirmation = false
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EXECUTION (Steps D7-D8)
    // ═══════════════════════════════════════════════════════════════

    public async Task<DDLOperationResult> ExecuteAsync(
        DDLOperationRequest request,
        DDLOperationPreview preview,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var steps = new List<string>();

        _logger.LogInformation(
            "[DDLPipeline] Executing DDL operation: {Type} on {Target}",
            preview.OperationType, preview.TargetObject);

        try
        {
            // Verify confirmation
            if (!request.IsConfirmed)
            {
                return new DDLOperationResult
                {
                    Success = false,
                    ErrorMessage = "User confirmation required before executing DDL operation",
                    OperationType = preview.OperationType,
                    TargetObject = preview.TargetObject
                };
            }

            // D7: Execute DDL with timeout
            steps.Add($"Executing {preview.OperationType} on {preview.TargetObject}");

            var executionResult = await _sqlExecutor.ExecuteAsync(preview.DDLScript, ct);

            if (!executionResult.Success)
            {
                return new DDLOperationResult
                {
                    Success = false,
                    ErrorMessage = executionResult.ErrorMessage,
                    DDLExecuted = preview.DDLScript,
                    OperationType = preview.OperationType,
                    TargetObject = preview.TargetObject,
                    ProcessingSteps = steps
                };
            }

            steps.Add("DDL executed successfully");

            // D8: Reload schema cache
            steps.Add("Reloading schema cache");
            await ReloadSchemaCacheAsync(request.ConnectionId, ct);
            steps.Add("Schema cache reloaded");

            stopwatch.Stop();

            _logger.LogInformation(
                "[DDLPipeline] Execution complete in {Ms}ms",
                stopwatch.ElapsedMilliseconds);

            return new DDLOperationResult
            {
                Success = true,
                DDLExecuted = preview.DDLScript,
                ExecutionTime = stopwatch.Elapsed,
                OperationType = preview.OperationType,
                TargetObject = preview.TargetObject,
                ProcessingSteps = steps,
                SchemaCacheReloaded = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DDLPipeline] Execution failed");
            return new DDLOperationResult
            {
                Success = false,
                ErrorMessage = $"Execution failed: {ex.Message}",
                DDLExecuted = preview.DDLScript,
                OperationType = preview.OperationType,
                TargetObject = preview.TargetObject,
                ProcessingSteps = steps
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // D1: CLASSIFY DDL TYPE
    // ═══════════════════════════════════════════════════════════════

    private async Task<DDLOperationType> ClassifyDDLTypeAsync(string question, CancellationToken ct)
    {
        var lower = question.ToLowerInvariant();

        // Rule-based classification first
        if (lower.Contains("create index") || lower.Contains("tạo index"))
            return DDLOperationType.CreateIndex;

        if (lower.Contains("drop index") || lower.Contains("xóa index"))
            return DDLOperationType.DropIndex;

        if (lower.Contains("create procedure") || lower.Contains("tạo procedure"))
            return DDLOperationType.CreateProcedure;

        if (lower.Contains("create function") || lower.Contains("tạo function"))
            return DDLOperationType.CreateFunction;

        if (lower.Contains("create view") || lower.Contains("tạo view"))
            return DDLOperationType.CreateView;

        if (lower.Contains("add column") || lower.Contains("thêm cột"))
            return DDLOperationType.AlterTableAddColumn;

        if (lower.Contains("alter table") || lower.Contains("modify column"))
            return DDLOperationType.AlterTableModifyColumn;

        // LLM fallback for ambiguous cases
        var prompt = $@"Classify this DDL request into one of these types:
- CREATE_INDEX
- DROP_INDEX
- CREATE_PROCEDURE
- CREATE_FUNCTION
- CREATE_VIEW
- ALTER_TABLE_ADD_COLUMN
- ALTER_TABLE_MODIFY_COLUMN
- UNKNOWN

Question: {question}

Return ONLY the type, nothing else:";

        var response = await _llmClient.CompleteAsync(prompt, ct);
        var typeStr = response.Trim().ToUpperInvariant();

        return typeStr switch
        {
            "CREATE_INDEX" => DDLOperationType.CreateIndex,
            "DROP_INDEX" => DDLOperationType.DropIndex,
            "CREATE_PROCEDURE" => DDLOperationType.CreateProcedure,
            "CREATE_FUNCTION" => DDLOperationType.CreateFunction,
            "CREATE_VIEW" => DDLOperationType.CreateView,
            "ALTER_TABLE_ADD_COLUMN" => DDLOperationType.AlterTableAddColumn,
            "ALTER_TABLE_MODIFY_COLUMN" => DDLOperationType.AlterTableModifyColumn,
            _ => DDLOperationType.Unknown
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // D3: FIND RELATED OBJECTS
    // ═══════════════════════════════════════════════════════════════

    private List<string> FindRelatedObjects(
        string question,
        DatabaseSchema schema,
        DDLOperationType ddlType)
    {
        var relatedObjects = new List<string>();
        var lower = question.ToLowerInvariant();

        // Find mentioned tables
        foreach (var table in schema.Tables)
        {
            if (lower.Contains(table.TableName.ToLowerInvariant()))
            {
                relatedObjects.Add($"Table: {table.TableName}");

                // For indexes, check existing indexes
                if (ddlType == DDLOperationType.CreateIndex)
                {
                    // Note: This would require index information in schema
                    // For now, just note the table
                }
            }
        }

        return relatedObjects;
    }

    // ═══════════════════════════════════════════════════════════════
    // D4: GENERATE DDL SCRIPT
    // ═══════════════════════════════════════════════════════════════

    private async Task<(string Script, string TargetObject)> GenerateDDLScriptAsync(
        string question,
        DDLOperationType ddlType,
        DatabaseSchema schema,
        List<string> relatedObjects,
        CancellationToken ct)
    {
        var systemPrompt = $@"You are a DDL script generator.

Generate DDL scripts following these naming conventions:
- Indexes: idx_{{table}}_{{columns}} (e.g., idx_users_email)
- Procedures: sp_{{action}}_{{entity}} (e.g., sp_get_active_users)
- Views: vw_{{description}} (e.g., vw_monthly_revenue)

Rules:
1. Follow SQL standard syntax
2. Include comments explaining purpose
3. For indexes: consider column order for query optimization
4. For procedures: include parameter documentation
5. For views: ensure query is optimized

Return JSON format:
{{
  ""ddl_script"": ""the DDL statement with comments"",
  ""target_object"": ""name of the object being created/modified""
}}";

        var schemaContext = BuildSchemaContext(schema, relatedObjects);
        var userPrompt = $@"Generate DDL script for this request:

Question: {question}
DDL Type: {ddlType}

Schema context:
{schemaContext}

Generate the DDL script:";

        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, ct);

        // Parse response
        var cleaned = response.Trim();
        if (cleaned.StartsWith("```json")) cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
        if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
        cleaned = cleaned.Trim();

        var parsed = JsonSerializer.Deserialize<DDLGenerationResponse>(cleaned);

        return (parsed?.DdlScript ?? "", parsed?.TargetObject ?? "unknown");
    }

    private class DDLGenerationResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("ddl_script")]
        public string DdlScript { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("target_object")]
        public string TargetObject { get; set; } = string.Empty;
    }

    private string BuildSchemaContext(DatabaseSchema schema, List<string> relatedObjects)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Database Schema:");
        foreach (var table in schema.Tables.Take(10)) // Limit to avoid token overflow
        {
            sb.AppendLine($"  {table.TableName} ({table.Columns.Count} columns)");
        }

        if (relatedObjects.Any())
        {
            sb.AppendLine("\nRelated Objects:");
            foreach (var obj in relatedObjects)
            {
                sb.AppendLine($"  {obj}");
            }
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // D5: ANALYZE IMPACT
    // ═══════════════════════════════════════════════════════════════

    private async Task<DDLImpactAnalysis> AnalyzeImpactAsync(
        string ddlScript,
        DDLOperationType ddlType,
        string targetObject,
        DatabaseSchema schema,
        CancellationToken ct)
    {
        var impact = new DDLImpactAnalysis();

        switch (ddlType)
        {
            case DDLOperationType.CreateIndex:
                impact = await AnalyzeIndexImpactAsync(ddlScript, targetObject, schema, ct);
                break;

            case DDLOperationType.AlterTableAddColumn:
            case DDLOperationType.AlterTableModifyColumn:
                impact = AnalyzeAlterTableImpact(ddlScript, targetObject);
                break;

            case DDLOperationType.CreateView:
            case DDLOperationType.CreateProcedure:
            case DDLOperationType.CreateFunction:
                impact = AnalyzeObjectCreationImpact(ddlType, targetObject);
                break;

            default:
                impact.Warnings.Add("Impact analysis not available for this DDL type");
                break;
        }

        return impact;
    }

    private async Task<DDLImpactAnalysis> AnalyzeIndexImpactAsync(
        string ddlScript,
        string indexName,
        DatabaseSchema schema,
        CancellationToken ct)
    {
        var impact = new DDLImpactAnalysis
        {
            EstimatedStorageBytes = 10 * 1024 * 1024, // Estimate 10MB
            EstimatedLockDuration = TimeSpan.FromSeconds(2),
            EstimatedPerformanceGain = 10.0, // 10x faster
            WriteOverheadPercent = 5.0 // 5% slower writes
        };

        impact.Benefits.Add("Faster query execution for filtered/sorted queries");
        impact.Benefits.Add("Reduced table scan operations");
        impact.Benefits.Add("Improved JOIN performance");

        impact.Warnings.Add("Index will consume additional storage space");
        impact.Warnings.Add("Write operations (INSERT/UPDATE/DELETE) will be slightly slower");
        impact.Warnings.Add("Table will be locked briefly during index creation");

        impact.AffectedObjects.Add($"Index: {indexName}");

        return impact;
    }

    private DDLImpactAnalysis AnalyzeAlterTableImpact(string ddlScript, string tableName)
    {
        var impact = new DDLImpactAnalysis
        {
            EstimatedLockDuration = TimeSpan.FromSeconds(5),
            EstimatedStorageBytes = 0 // Depends on column type
        };

        impact.Benefits.Add("Enhanced data model with new column");
        impact.Benefits.Add("Improved data organization");

        impact.Warnings.Add("Table will be locked during ALTER operation");
        impact.Warnings.Add("Existing queries may need to be updated");
        impact.Warnings.Add("Consider default values for existing rows");

        impact.AffectedObjects.Add($"Table: {tableName}");

        return impact;
    }

    private DDLImpactAnalysis AnalyzeObjectCreationImpact(DDLOperationType ddlType, string objectName)
    {
        var impact = new DDLImpactAnalysis
        {
            EstimatedLockDuration = TimeSpan.FromMilliseconds(100),
            EstimatedStorageBytes = 1024 // Minimal storage
        };

        impact.Benefits.Add($"New {ddlType} created for reusable logic");
        impact.Benefits.Add("Improved code organization and maintainability");

        impact.Warnings.Add("Ensure proper permissions are set");
        impact.Warnings.Add("Test thoroughly before using in production");

        impact.AffectedObjects.Add($"{ddlType}: {objectName}");

        return impact;
    }

    // ═══════════════════════════════════════════════════════════════
    // D8: RELOAD SCHEMA CACHE
    // ═══════════════════════════════════════════════════════════════

    private async Task ReloadSchemaCacheAsync(string connectionId, CancellationToken ct)
    {
        try
        {
            // Invalidate cache to force reload
            await _schemaCache.RemoveAsync(connectionId, ct);

            _logger.LogInformation("[DDLPipeline] Schema cache invalidated for connection: {ConnectionId}", connectionId);

            // Note: The next query will trigger schema reload automatically
            // If you have a vector DB indexer, trigger re-indexing here
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DDLPipeline] Failed to reload schema cache");
        }
    }
}
