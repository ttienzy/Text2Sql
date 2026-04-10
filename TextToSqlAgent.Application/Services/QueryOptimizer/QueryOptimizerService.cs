using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Main orchestrator for query optimization
/// Implements 4-layer pipeline: Static Analysis â†’ Schema Enrichment â†’ LLM Rewrite â†’ Verification
/// </summary>
public class QueryOptimizerService
{
    private readonly StaticAnalyzer _staticAnalyzer;
    private readonly SchemaEnricher _schemaEnricher;
    private readonly QueryNormalizer _queryNormalizer;
    private readonly ComplexityDetector _complexityDetector;
    private readonly ExecutionPlanService _executionPlanService;
    private readonly ColumnStatisticsService _columnStatisticsService;
    private readonly ContextBudgetManager _contextBudgetManager;
    private readonly IDistributedCache _cache;
    private readonly ILLMClient _llmClient;
    private readonly ILogger<QueryOptimizerService> _logger;

    public QueryOptimizerService(
        StaticAnalyzer staticAnalyzer,
        SchemaEnricher schemaEnricher,
        QueryNormalizer queryNormalizer,
        ComplexityDetector complexityDetector,
        ExecutionPlanService executionPlanService,
        ColumnStatisticsService columnStatisticsService,
        ContextBudgetManager contextBudgetManager,
        IDistributedCache cache,
        ILLMClient llmClient,
        ILogger<QueryOptimizerService> logger)
    {
        _staticAnalyzer = staticAnalyzer;
        _schemaEnricher = schemaEnricher;
        _queryNormalizer = queryNormalizer;
        _complexityDetector = complexityDetector;
        _executionPlanService = executionPlanService;
        _columnStatisticsService = columnStatisticsService;
        _contextBudgetManager = contextBudgetManager;
        _cache = cache;
        _llmClient = llmClient;
        _logger = logger;
    }

    /// <summary>
    /// Resolve prompt file path by searching up the directory tree
    /// </summary>
    private string? ResolvePromptFilePath(string relativePath)
    {
        var possibleDirs = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var startDir in possibleDirs)
        {
            var currentDir = new DirectoryInfo(startDir);
            while (currentDir != null)
            {
                var combinedPath = Path.Combine(currentDir.FullName, relativePath);
                if (File.Exists(combinedPath))
                {
                    return Path.GetFullPath(combinedPath);
                }
                currentDir = currentDir.Parent;
            }
        }

        // Direct fallback
        if (File.Exists(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        return null;
    }

    /// <summary>
    /// Clean JSON response by removing markdown code blocks
    /// </summary>
    private string CleanJsonResponse(string response)
    {
        var cleaned = response.Trim();

        // Remove markdown code blocks
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7);
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Substring(3);
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Optimizes SQL query using 4-layer pipeline
    /// </summary>
    public async Task<OptimizationResult> OptimizeAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check cache first
            var connectionScope = ConnectionFingerprint.Compute(connectionString);
            var cacheKey = _queryNormalizer.GenerateCacheKey(sql, connectionScope);
            var cachedResult = await _cache.GetStringAsync(cacheKey, cancellationToken);

            if (!string.IsNullOrEmpty(cachedResult))
            {
                _logger.LogInformation("Cache hit for query optimization");
                return JsonSerializer.Deserialize<OptimizationResult>(cachedResult)!;
            }

            // Layer 1: Static Analysis (~50ms)
            var metadata = await _staticAnalyzer.AnalyzeAsync(sql, cancellationToken);
            // Layer 2: Schema Enrichment (~5-10ms with Redis)
            var schemaContext = await _schemaEnricher.EnrichSchemaAsync(
                metadata.Tables,
                connectionString,
                cancellationToken);
            AppendSchemaValidationIssues(metadata, schemaContext);

            // Determine model based on complexity
            var modelType = _complexityDetector.DetermineModel(metadata);
            var modelName = _complexityDetector.GetModelName(modelType);

            // Hard-stop on semantic schema errors
            if (schemaContext.MissingTables.Any())
            {
                var missingTables = string.Join(", ", schemaContext.MissingTables.OrderBy(t => t));
                return new OptimizationResult
                {
                    OriginalSql = sql,
                    OptimizedSql = sql,
                    IsChanged = false,
                    Severity = "critical",
                    DetectedIssues = metadata.DetectedIssues,
                    Explanation = $"Cannot optimize because referenced table(s) do not exist in this database: {missingTables}.",
                    EstimatedImprovement = "Fix table/schema references first",
                    ComplexityScore = metadata.ComplexityScore,
                    ModelUsed = modelName
                };
            }

            // If no issues detected and query is simple, return as-is
            if (!metadata.DetectedIssues.Any() && metadata.ComplexityScore <= 3)
            {
                return new OptimizationResult
                {
                    OriginalSql = sql,
                    OptimizedSql = sql,
                    IsChanged = false,
                    Severity = "ok",
                    Explanation = "Query is already in good shape. No optimization needed.",
                    ComplexityScore = metadata.ComplexityScore,
                    ModelUsed = modelName
                };
            }

            // Layer 3: LLM Optimization (~2-5s)
            var optimizationResult = await OptimizeWithLLMAsync(
                sql,
                metadata,
                schemaContext,
                modelName,
                connectionString,
                cancellationToken);

            // Cache result for 24 hours
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            };

            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(optimizationResult),
                cacheOptions,
                cancellationToken);

            return optimizationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing query");
            throw;
        }
    }

    /// <summary>
    /// Optimizes query using LLM with focused prompts and token budget management
    /// </summary>
    private async Task<OptimizationResult> OptimizeWithLLMAsync(
        string sql,
        QueryMetadata metadata,
        SchemaContext schemaContext,
        string modelName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        // ========== Phase 4: Pre-flight Analysis ==========
        var preFlightAnalysis = await _executionPlanService.GetPreFlightAnalysisAsync(
            sql, connectionString, cancellationToken);

        // Early exit if no optimization needed
        if (!preFlightAnalysis.NeedsOptimization && !metadata.DetectedIssues.Any())
        {
            return new OptimizationResult
            {
                OriginalSql = sql,
                OptimizedSql = sql,
                IsChanged = false,
                Severity = "ok",
                Explanation = "Query is already optimal. No issues detected in static analysis or execution plan.",
                ComplexityScore = metadata.ComplexityScore,
                ModelUsed = modelName
            };
        }

        // ========== Phase 2: Column Statistics ==========
        var columnStats = await GetColumnStatisticsForQueryAsync(
            metadata, connectionString, cancellationToken);

        // ========== Phase 4: Compatibility Level Check ==========
        var compatLevel = await GetCompatibilityLevelAsync(connectionString, cancellationToken);
        var pspActive = compatLevel >= 160;

        // ========== Phase 4: Build Prioritized Context with Token Budget ==========
        var contextSections = _contextBudgetManager.BuildPrioritizedContext(
            preFlightAnalysis, columnStats, metadata.DetectedIssues, schemaContext);

        // Load prompt template
        var promptPath = ResolvePromptFilePath("Prompts/QueryOptimizer/optimize-query.skprompt.txt");

        if (promptPath == null)
        {
            _logger.LogError("Prompt template not found: Prompts/QueryOptimizer/optimize-query.skprompt.txt");
            throw new FileNotFoundException("Query optimizer prompt template not found");
        }

        var promptTemplate = await System.IO.File.ReadAllTextAsync(promptPath, cancellationToken);

        // Replace placeholders
        var prompt = promptTemplate
            .Replace("{{$execution_plan_available}}", preFlightAnalysis.CanGetExecutionPlan.ToString())
            .Replace("{{$execution_plan_cost}}", preFlightAnalysis.EstimatedCost.ToString("F2"))
            .Replace("{{$execution_plan_rows}}", preFlightAnalysis.EstimatedRows.ToString("N0"))
            .Replace("{{$context_sections}}", contextSections)
            .Replace("{{$original_sql}}", sql)
            .Replace("{{$compatibility_level}}", compatLevel.ToString())
            .Replace("{{$psp_active}}", pspActive ? "Yes (SQL Server 2022)" : "No");

        // Call LLM using ILLMClient
        var responseText = await _llmClient.CompleteAsync(prompt, cancellationToken);

        // Clean and parse JSON response
        var cleanedResponse = CleanJsonResponse(responseText);

        _logger.LogDebug("[QueryOptimizer] LLM Response (cleaned): {Response}", cleanedResponse);

        var llmResponse = ParseLLMResponse(cleanedResponse);

        return MapToOptimizationResult(sql, metadata, llmResponse, preFlightAnalysis, modelName);
    }

    /// <summary>
    /// Get column statistics for all critical columns in the query
    /// </summary>
    private async Task<Dictionary<string, ColumnStatistics>> GetColumnStatisticsForQueryAsync(
        QueryMetadata metadata,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var criticalColumns = metadata.GetCriticalColumns();
        if (!criticalColumns.Any())
        {
            return new Dictionary<string, ColumnStatistics>();
        }

        var columnStats = new Dictionary<string, ColumnStatistics>();

        // Parallel stats gathering with timeout per column
        var statsTasks = metadata.Tables.Select(async table =>
        {
            var tableColumns = criticalColumns
                .Where(c => BelongsToTable(c, table))
                .ToList();

            if (!tableColumns.Any()) return;

            foreach (var col in tableColumns)
            {
                try
                {
                    // 5 second timeout per column
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));

                    var stats = await _columnStatisticsService.GetColumnStatisticsAsync(
                        table, col, connectionString, cts.Token);

                    if (stats != null)
                    {
                        lock (columnStats)
                        {
                            columnStats[$"{table}.{col}"] = stats;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Statistics timeout for {Table}.{Column}", table, col);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get stats for {Table}.{Column}", table, col);
                }
            }
        });

        await Task.WhenAll(statsTasks);

        return columnStats;
    }

    /// <summary>
    /// Get SQL Server compatibility level
    /// </summary>
    private async Task<int> GetCompatibilityLevelAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()";
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result is byte b ? b : 150; // default 150 if fail
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get compatibility level, defaulting to 150");
            return 150;
        }
    }

    /// <summary>
    /// Parse LLM response with robust error handling
    /// </summary>
    private LLMOptimizationResponse ParseLLMResponse(string cleanedResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(cleanedResponse);
            var root = document.RootElement;

            return new LLMOptimizationResponse
            {
                OptimizedSql = GetJsonString(root, "optimized_sql", "optimizedSql", "OptimizedSql"),
                IsChanged = GetJsonBool(root, "is_changed", "isChanged", "IsChanged"),
                Severity = GetJsonString(root, "severity", "Severity", defaultValue: "ok"),
                IssuesFixed = GetJsonStringList(root, "issues_fixed", "issuesFixed", "IssuesFixed"),
                Explanation = GetJsonString(root, "explanation", "Explanation"),
                EstimatedImprovement = GetJsonString(root, "estimated_improvement", "estimatedImprovement", "EstimatedImprovement"),
                IndexSuggestions = GetJsonStringList(root, "index_suggestions", "indexSuggestions", "IndexSuggestions"),
                DataSkewNotes = GetJsonNullableString(root, "data_skew_notes", "dataSkewNotes", "DataSkewNotes"),
                PspRecommendation = GetJsonNullableString(root, "psp_recommendation", "pspRecommendation", "PspRecommendation")
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM response as JSON: {Response}", cleanedResponse);
            return new LLMOptimizationResponse
            {
                OptimizedSql = string.Empty,
                IsChanged = false,
                Severity = "error",
                Explanation = $"Failed to parse LLM response: {ex.Message}"
            };
        }
    }

    private static string GetJsonString(JsonElement root, string primary, string alternate, string? fallback = null, string defaultValue = "")
    {
        foreach (var key in new[] { primary, alternate, fallback })
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                root.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? defaultValue;
            }
        }

        return defaultValue;
    }

    private static string? GetJsonNullableString(JsonElement root, string primary, string alternate, string? fallback = null)
    {
        foreach (var key in new[] { primary, alternate, fallback })
        {
            if (string.IsNullOrWhiteSpace(key) || !root.TryGetProperty(key, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null => null,
                _ => value.GetRawText()
            };
        }

        return null;
    }

    private static bool GetJsonBool(JsonElement root, string primary, string alternate, string? fallback = null)
    {
        foreach (var key in new[] { primary, alternate, fallback })
        {
            if (string.IsNullOrWhiteSpace(key) || !root.TryGetProperty(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var boolValue))
            {
                return boolValue;
            }
        }

        return false;
    }

    private static List<string> GetJsonStringList(JsonElement root, string primary, string alternate, string? fallback = null)
    {
        foreach (var key in new[] { primary, alternate, fallback })
        {
            if (string.IsNullOrWhiteSpace(key) || !root.TryGetProperty(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToList();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var single = value.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    return new List<string> { single };
                }
            }
        }

        return new List<string>();
    }

    /// <summary>
    /// Map LLM response to OptimizationResult
    /// </summary>
    private OptimizationResult MapToOptimizationResult(
        string originalSql,
        QueryMetadata metadata,
        LLMOptimizationResponse llmResponse,
        PreFlightAnalysis preFlightAnalysis,
        string modelName)
    {
        var optimizedSql = string.IsNullOrWhiteSpace(llmResponse.OptimizedSql)
            ? originalSql
            : llmResponse.OptimizedSql;

        return new OptimizationResult
        {
            OriginalSql = originalSql,
            OptimizedSql = optimizedSql,
            IsChanged = llmResponse.IsChanged && !string.Equals(originalSql, optimizedSql, StringComparison.Ordinal),
            Severity = DetermineOverallSeverity(metadata.DetectedIssues, preFlightAnalysis, llmResponse.Severity),
            DetectedIssues = metadata.DetectedIssues,
            IssuesFixed = llmResponse.IssuesFixed,
            Explanation = llmResponse.Explanation,
            EstimatedImprovement = llmResponse.EstimatedImprovement,
            IndexSuggestions = llmResponse.IndexSuggestions,
            ComplexityScore = metadata.ComplexityScore,
            ModelUsed = modelName,
            PreFlightAnalysis = preFlightAnalysis
        };
    }

    private void AppendSchemaValidationIssues(QueryMetadata metadata, SchemaContext schemaContext)
    {
        if (schemaContext.MissingTables.Any())
        {
            foreach (var table in schemaContext.MissingTables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                metadata.DetectedIssues.Add(new AntiPattern
                {
                    Code = "SEM-01",
                    Severity = Severity.Critical,
                    Title = "Referenced table does not exist",
                    Description = $"Table '{table}' was not found in the selected database connection.",
                    Impact = "Query cannot compile or execute",
                    Category = PatternCategory.Logic
                });
            }
        }

        if (schemaContext.EnrichmentWarnings.Any())
        {
            foreach (var warning in schemaContext.EnrichmentWarnings.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                metadata.DetectedIssues.Add(new AntiPattern
                {
                    Code = "SEM-02",
                    Severity = Severity.Warning,
                    Title = "Schema metadata unavailable",
                    Description = warning,
                    Impact = "Optimization recommendations may be incomplete",
                    Category = PatternCategory.CodeQuality
                });
            }
        }
    }

    private string DetermineOverallSeverity(
        List<AntiPattern> issues,
        PreFlightAnalysis preFlight,
        string? llmSeverity)
    {
        if (issues.Any(i => i.Severity is Severity.Critical or Severity.Error))
        {
            return "critical";
        }

        if (preFlight.Warnings.Any(w => w.Severity is WarningSeverity.Critical or WarningSeverity.High))
        {
            return "critical";
        }

        if (issues.Any(i => i.Severity is Severity.Warning or Severity.Serious) ||
            preFlight.Warnings.Any(w => w.Severity == WarningSeverity.Medium))
        {
            return "warning";
        }

        return string.IsNullOrWhiteSpace(llmSeverity)
            ? "ok"
            : llmSeverity.ToLowerInvariant();
    }

    /// <summary>
    /// Check if column belongs to table (simple heuristic)
    /// </summary>
    private bool BelongsToTable(string columnName, string tableName)
    {
        // Simple heuristic: assume column belongs to table if not qualified
        // In production, would need proper AST analysis
        return true;
    }

    /// <summary>
    /// Optimizes query with execution plan comparison (Sprint 2 feature)
    /// </summary>
    public async Task<OptimizationResultWithPlan> OptimizeWithPlanComparisonAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        // First, get basic optimization
        var basicResult = await OptimizeAsync(sql, connectionString, cancellationToken);

        // Skip execution-plan comparison when query has blocking semantic errors
        if (basicResult.DetectedIssues.Any(i => i.Code == "SEM-01"))
        {
            return new OptimizationResultWithPlan
            {
                OriginalSql = basicResult.OriginalSql,
                OptimizedSql = basicResult.OptimizedSql,
                IsChanged = false,
                Severity = basicResult.Severity,
                DetectedIssues = basicResult.DetectedIssues,
                IssuesFixed = basicResult.IssuesFixed,
                Explanation = basicResult.Explanation,
                EstimatedImprovement = basicResult.EstimatedImprovement,
                IndexSuggestions = basicResult.IndexSuggestions,
                ComplexityScore = basicResult.ComplexityScore,
                ModelUsed = basicResult.ModelUsed,
                PreFlightAnalysis = basicResult.PreFlightAnalysis,
                PlanComparison = null
            };
        }

        // Always get execution plan, even if query wasn't changed
        // This allows users to see the current query's execution plan
        try
        {
            var originalPlan = await _executionPlanService.GetEstimatedPlanAsync(
                basicResult.OriginalSql,
                connectionString,
                cancellationToken);

            // If query wasn't changed, show plan for original query only
            if (!basicResult.IsChanged)
            {
                return new OptimizationResultWithPlan
                {
                    OriginalSql = basicResult.OriginalSql,
                    OptimizedSql = basicResult.OptimizedSql,
                    IsChanged = false,
                    Severity = basicResult.Severity,
                    DetectedIssues = basicResult.DetectedIssues,
                    IssuesFixed = basicResult.IssuesFixed,
                    Explanation = basicResult.Explanation,
                    EstimatedImprovement = basicResult.EstimatedImprovement,
                    IndexSuggestions = basicResult.IndexSuggestions,
                    ComplexityScore = basicResult.ComplexityScore,
                    ModelUsed = basicResult.ModelUsed,
                    PreFlightAnalysis = basicResult.PreFlightAnalysis,
                    PlanComparison = new PlanComparison
                    {
                        OriginalCost = originalPlan.EstimatedTotalCost,
                        OptimizedCost = originalPlan.EstimatedTotalCost,
                        ImprovementFactor = 1.0,
                        ImprovementPercentage = 0,
                        IsImproved = false,
                        ImprovementDescription = "Query is already optimal - no changes needed",
                        OriginalOperators = originalPlan.Operators,
                        OptimizedOperators = originalPlan.Operators,
                        OriginalWarnings = originalPlan.Warnings,
                        OptimizedWarnings = originalPlan.Warnings
                    }
                };
            }

            // Query was changed - compare both plans
            var optimizedPlan = await _executionPlanService.GetEstimatedPlanAsync(
                basicResult.OptimizedSql,
                connectionString,
                cancellationToken);

            var planComparison = await _executionPlanService.ComparePlansAsync(
                basicResult.OriginalSql,
                basicResult.OptimizedSql,
                connectionString,
                cancellationToken);

            return new OptimizationResultWithPlan
            {
                OriginalSql = basicResult.OriginalSql,
                OptimizedSql = basicResult.OptimizedSql,
                IsChanged = basicResult.IsChanged,
                Severity = basicResult.Severity,
                DetectedIssues = basicResult.DetectedIssues,
                IssuesFixed = basicResult.IssuesFixed,
                Explanation = basicResult.Explanation,
                EstimatedImprovement = planComparison.ImprovementDescription,
                IndexSuggestions = basicResult.IndexSuggestions,
                ComplexityScore = basicResult.ComplexityScore,
                ModelUsed = basicResult.ModelUsed,
                PreFlightAnalysis = basicResult.PreFlightAnalysis,
                PlanComparison = planComparison
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get execution plans, returning basic optimization without plan data");

            return new OptimizationResultWithPlan
            {
                OriginalSql = basicResult.OriginalSql,
                OptimizedSql = basicResult.OptimizedSql,
                IsChanged = basicResult.IsChanged,
                Severity = basicResult.Severity,
                DetectedIssues = basicResult.DetectedIssues,
                IssuesFixed = basicResult.IssuesFixed,
                Explanation = basicResult.Explanation + "\n\nâš ï¸ Note: Could not retrieve execution plan data.",
                EstimatedImprovement = basicResult.EstimatedImprovement,
                IndexSuggestions = basicResult.IndexSuggestions,
                ComplexityScore = basicResult.ComplexityScore,
                ModelUsed = basicResult.ModelUsed,
                PreFlightAnalysis = basicResult.PreFlightAnalysis,
                PlanComparison = null
            };
        }
    }
}

/// <summary>
/// LLM optimization response
/// </summary>
internal class LLMOptimizationResponse
{
    public string OptimizedSql { get; set; } = string.Empty;
    public bool IsChanged { get; set; }
    public string Severity { get; set; } = "ok";
    public System.Collections.Generic.List<string> IssuesFixed { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public string EstimatedImprovement { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> IndexSuggestions { get; set; } = new();
    public string? DataSkewNotes { get; set; }
    public string? PspRecommendation { get; set; }
}

/// <summary>
/// Optimization result
/// </summary>
public class OptimizationResult
{
    public string OriginalSql { get; set; } = string.Empty;
    public string OptimizedSql { get; set; } = string.Empty;
    public bool IsChanged { get; set; }
    public string Severity { get; set; } = "ok";
    public System.Collections.Generic.List<AntiPattern> DetectedIssues { get; set; } = new();
    public System.Collections.Generic.List<string> IssuesFixed { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public string EstimatedImprovement { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> IndexSuggestions { get; set; } = new();
    public int ComplexityScore { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public PreFlightAnalysis? PreFlightAnalysis { get; set; }
}

/// <summary>
/// Optimization result with execution plan comparison
/// </summary>
public class OptimizationResultWithPlan : OptimizationResult
{
    public PlanComparison? PlanComparison { get; set; }
}

