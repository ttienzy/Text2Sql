using System.Data;
using Microsoft.Data.SqlClient;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Service for getting and parsing SQL Server execution plans using SHOWPLAN_XML.
/// Production-safe: NO query execution, only estimated plans.
/// </summary>
public class ExecutionPlanService
{
    private readonly ILogger<ExecutionPlanService> _logger;

    public ExecutionPlanService(ILogger<ExecutionPlanService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check if the connection has VIEW DATABASE STATE permission.
    /// Required for getting execution plans.
    /// </summary>
    public async Task<bool> CanGetExecutionPlanAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = @"
                SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE')";

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            using var cmd = new SqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            return result is int i && i == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot check VIEW DATABASE STATE permission");
            return false;
        }
    }

    /// <summary>
    /// Get pre-flight analysis with full execution plan insights.
    /// Includes permission check, warnings, cost drivers, and index recommendations.
    /// Gracefully degrades when permissions are missing.
    /// </summary>
    public async Task<PreFlightAnalysis> GetPreFlightAnalysisAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        // Permission check FIRST
        var hasPermission = await CanGetExecutionPlanAsync(connectionString, cancellationToken);

        if (!hasPermission)
        {
            _logger.LogWarning("No VIEW DATABASE STATE permission. Falling back to static analysis.");
            return new PreFlightAnalysis
            {
                CanGetExecutionPlan = false,
                NeedsOptimization = true,
                Warnings = new List<PlanWarning>
                {
                    new()
                    {
                        Type = WarningType.Other,
                        Severity = WarningSeverity.Info,
                        RawWarning = "Missing permission",
                        Description = "Execution plan analysis unavailable (missing VIEW DATABASE STATE permission).",
                        Recommendation = "Grant VIEW DATABASE STATE to enable full execution plan analysis."
                    }
                }
            };
        }

        try
        {
            var planXml = await GetEstimatedPlanXmlAsync(sql, connectionString, cancellationToken);
            var parsedPlan = ParseExecutionPlanXml(planXml);

            return new PreFlightAnalysis
            {
                CanGetExecutionPlan = true,
                EstimatedCost = parsedPlan.EstimatedTotalCost,
                EstimatedRows = parsedPlan.EstimatedRows,
                CostDrivers = IdentifyCostDrivers(parsedPlan.Operators),
                Warnings = ParseWarnings(parsedPlan.Warnings),
                IndexRecommendations = BuildIndexRecommendations(parsedPlan.MissingIndexes),
                ImplicitConversions = DetectImplicitConversions(parsedPlan),
                MissingStatistics = ExtractMissingStatistics(parsedPlan),
                NeedsOptimization = DetermineIfOptimizationNeeded(parsedPlan),
                HasStaleStatistics = parsedPlan.Warnings.Any(w => w.Contains("ColumnsWithNoStatistics"))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution plan for query");
            return new PreFlightAnalysis
            {
                CanGetExecutionPlan = false,
                NeedsOptimization = true,
                Warnings = new List<PlanWarning>
                {
                    new()
                    {
                        Type = WarningType.Other,
                        Severity = WarningSeverity.Medium,
                        RawWarning = ex.Message,
                        Description = $"Failed to retrieve execution plan: {ex.Message}",
                        Recommendation = "Check query syntax and database connectivity."
                    }
                }
            };
        }
    }

    /// <summary>
    /// Get estimated execution plan XML without executing the query.
    /// </summary>
    private async Task<string> GetEstimatedPlanXmlAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable SHOWPLAN_XML - returns plan WITHOUT executing query
        using var enableCommand = connection.CreateCommand();
        enableCommand.CommandText = "SET SHOWPLAN_XML ON";
        await enableCommand.ExecuteNonQueryAsync(cancellationToken);

        // Get the execution plan XML
        using var planCommand = connection.CreateCommand();
        planCommand.CommandText = sql;
        planCommand.CommandTimeout = 30;

        string? planXml = null;
        using var reader = await planCommand.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            planXml = reader.GetString(0);
        }

        // Disable SHOWPLAN_XML
        await reader.CloseAsync();
        using var disableCommand = connection.CreateCommand();
        disableCommand.CommandText = "SET SHOWPLAN_XML OFF";
        await disableCommand.ExecuteNonQueryAsync(cancellationToken);

        if (string.IsNullOrEmpty(planXml))
        {
            throw new InvalidOperationException("Failed to retrieve execution plan XML");
        }

        return planXml;
    }

    /// <summary>
    /// Get estimated execution plan for a query WITHOUT executing it.
    /// Uses SET SHOWPLAN_XML ON - production-safe, no data modification.
    /// </summary>
    public async Task<ExecutionPlan> GetEstimatedPlanAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var planXml = await GetEstimatedPlanXmlAsync(sql, connectionString, cancellationToken);
            return ParseExecutionPlanXml(planXml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution plan for query: {Sql}", sql);
            throw;
        }
    }

    /// <summary>
    /// Compare execution plans of original vs optimized query.
    /// Returns improvement metrics and operator differences.
    /// </summary>
    public async Task<PlanComparison> ComparePlansAsync(
        string originalSql,
        string optimizedSql,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var originalPlan = await GetEstimatedPlanAsync(originalSql, connectionString, cancellationToken);
        var optimizedPlan = await GetEstimatedPlanAsync(optimizedSql, connectionString, cancellationToken);

        var improvementFactor = originalPlan.EstimatedTotalCost > 0
            ? originalPlan.EstimatedTotalCost / optimizedPlan.EstimatedTotalCost
            : 1.0;
        var improvementPercentage = originalPlan.EstimatedTotalCost > 0
            ? (1 - (optimizedPlan.EstimatedTotalCost / originalPlan.EstimatedTotalCost)) * 100
            : 0;

        return new PlanComparison
        {
            OriginalCost = originalPlan.EstimatedTotalCost,
            OptimizedCost = optimizedPlan.EstimatedTotalCost,
            ImprovementFactor = improvementFactor,
            ImprovementPercentage = improvementPercentage,
            OriginalOperators = originalPlan.Operators,
            OptimizedOperators = optimizedPlan.Operators,
            OriginalWarnings = originalPlan.Warnings,
            OptimizedWarnings = optimizedPlan.Warnings,
            IsImproved = optimizedPlan.EstimatedTotalCost < originalPlan.EstimatedTotalCost,
            ImprovementDescription = GetImprovementDescription(improvementFactor)
        };
    }

    /// <summary>
    /// Parse SHOWPLAN_XML to extract execution plan metrics.
    /// Enhanced to extract warnings, missing indexes, and implicit conversions.
    /// </summary>
    private ExecutionPlan ParseExecutionPlanXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var stmtSimple = doc.Descendants(ns + "StmtSimple").FirstOrDefault();
        if (stmtSimple == null)
        {
            throw new InvalidOperationException("Invalid execution plan XML: StmtSimple element not found");
        }

        var plan = new ExecutionPlan
        {
            EstimatedTotalCost = double.Parse(stmtSimple.Attribute("StatementSubTreeCost")?.Value ?? "0"),
            EstimatedRows = long.Parse(stmtSimple.Attribute("StatementEstRows")?.Value ?? "0"),
            Operators = new List<PlanOperator>(),
            Warnings = new List<string>(),
            MissingIndexes = new List<MissingIndex>()
        };

        // Extract operators
        var relOps = doc.Descendants(ns + "RelOp");
        foreach (var relOp in relOps)
        {
            var op = new PlanOperator
            {
                Type = relOp.Attribute("PhysicalOp")?.Value ?? "Unknown",
                LogicalOp = relOp.Attribute("LogicalOp")?.Value ?? "Unknown",
                EstimatedCost = double.Parse(relOp.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "0"),
                EstimatedRows = double.Parse(relOp.Attribute("EstimateRows")?.Value ?? "0"),
                EstimatedCPU = double.Parse(relOp.Attribute("EstimateCPU")?.Value ?? "0"),
                EstimatedIO = double.Parse(relOp.Attribute("EstimateIO")?.Value ?? "0")
            };

            // Extract object name if available (for Index Scan/Seek)
            var indexScan = relOp.Descendants(ns + "IndexScan").FirstOrDefault();
            if (indexScan != null)
            {
                var obj = indexScan.Descendants(ns + "Object").FirstOrDefault();
                if (obj != null)
                {
                    op.ObjectName = $"{obj.Attribute("Schema")?.Value}.{obj.Attribute("Table")?.Value}";
                    op.IndexName = obj.Attribute("Index")?.Value;
                }
            }

            plan.Operators.Add(op);
        }

        // Extract warnings with detailed information
        var warningElements = doc.Descendants(ns + "Warnings");
        foreach (var warnEl in warningElements)
        {
            foreach (var child in warnEl.Elements())
            {
                var warningType = child.Name.LocalName;
                plan.Warnings.Add(warningType);

                // Extract missing statistics columns
                if (warningType == "ColumnsWithNoStatistics")
                {
                    var cols = child.Descendants(ns + "ColumnReference")
                        .Select(c => c.Attribute("Column")?.Value)
                        .Where(c => c != null)
                        .ToList();

                    if (cols.Any())
                    {
                        plan.Warnings.Add($"Missing stats on: {string.Join(", ", cols)}");
                    }
                }
            }
        }

        // Extract missing indexes
        foreach (var mg in doc.Descendants(ns + "MissingIndexGroup"))
        {
            var impact = double.TryParse(mg.Attribute("Impact")?.Value, out var imp) ? imp : 0;
            var idx = mg.Descendants(ns + "MissingIndex").FirstOrDefault();
            if (idx == null) continue;

            plan.MissingIndexes.Add(new MissingIndex
            {
                Database = idx.Attribute("Database")?.Value ?? "",
                Schema = idx.Attribute("Schema")?.Value ?? "dbo",
                Table = idx.Attribute("Table")?.Value ?? "",
                Impact = impact,
                EqualityColumns = GetColumnsByUsage(idx, ns, "EQUALITY"),
                InequalityColumns = GetColumnsByUsage(idx, ns, "INEQUALITY"),
                IncludedColumns = GetColumnsByUsage(idx, ns, "INCLUDE")
            });
        }

        // Detect implicit conversions
        var implicitConverts = doc.Descendants(ns + "ScalarOperator")
            .Where(so => so.Attribute("ScalarString")?.Value?.Contains("CONVERT_IMPLICIT") == true);

        foreach (var convert in implicitConverts)
        {
            var scalarString = convert.Attribute("ScalarString")?.Value;
            if (!string.IsNullOrEmpty(scalarString))
            {
                plan.Warnings.Add($"ImplicitConversion: {scalarString}");
            }
        }

        return plan;
    }

    /// <summary>
    /// Extract columns by usage type from missing index XML.
    /// </summary>
    private List<string> GetColumnsByUsage(XElement index, XNamespace ns, string usage)
    {
        return index.Elements(ns + "ColumnGroup")
            .Where(cg => cg.Attribute("Usage")?.Value == usage)
            .SelectMany(cg => cg.Elements(ns + "Column"))
            .Select(c => c.Attribute("Name")?.Value ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    private string GetImprovementDescription(double factor)
    {
        if (factor >= 100) return "~100x+ faster";
        if (factor >= 50) return $"~{(int)factor}x faster";
        if (factor >= 10) return $"~{(int)factor}x faster";
        if (factor >= 2) return $"~{factor:F1}x faster";
        if (factor > 1.1) return $"{((factor - 1) * 100):F0}% faster";
        if (factor > 0.9) return "Similar performance";
        return "No improvement";
    }

    /// <summary>
    /// Identify cost drivers from execution plan operators.
    /// Returns top expensive operations with actionable recommendations.
    /// </summary>
    private List<CostDriver> IdentifyCostDrivers(List<PlanOperator> operators)
    {
        var costDrivers = new List<CostDriver>();

        // Sort by cost descending, take top 5
        var topOperators = operators
            .OrderByDescending(op => op.EstimatedCost)
            .Take(5);

        foreach (var op in topOperators)
        {
            var driver = new CostDriver
            {
                OperatorType = op.Type,
                Cost = op.EstimatedCost,
                Rows = op.EstimatedRows,
                ObjectName = op.ObjectName,
                IndexName = op.IndexName,
                Description = FormatCostDriverDescription(op)
            };

            // Add actionable recommendation
            driver.Recommendation = op.Type switch
            {
                "Clustered Index Scan" or "Table Scan" =>
                    "⚠️ Full scan — consider adding a covering index",
                "Nested Loops" when op.EstimatedRows > 10000 =>
                    "⚠️ Nested loop on large dataset — consider hash join or merge join",
                "Sort" when op.EstimatedCost > 1.0 =>
                    "⚠️ Expensive sort — consider adding index to avoid sort",
                "Hash Match" =>
                    "ℹ️ Hash join detected — may benefit from indexed JOIN columns",
                "Key Lookup" =>
                    "⚠️ Key lookup — consider adding INCLUDE columns to covering index",
                _ => null
            };

            costDrivers.Add(driver);
        }

        return costDrivers;
    }

    /// <summary>
    /// Format cost driver description with operator details.
    /// </summary>
    private string FormatCostDriverDescription(PlanOperator op)
    {
        var desc = op.Type;
        if (!string.IsNullOrEmpty(op.ObjectName)) desc += $" on {op.ObjectName}";
        if (!string.IsNullOrEmpty(op.IndexName)) desc += $" using {op.IndexName}";
        desc += $" (Cost: {op.EstimatedCost:F2}, Rows: {op.EstimatedRows:N0})";
        return desc;
    }

    /// <summary>
    /// Parse raw warnings into structured PlanWarning objects.
    /// </summary>
    private List<PlanWarning> ParseWarnings(List<string> rawWarnings)
    {
        var result = new List<PlanWarning>();

        foreach (var raw in rawWarnings)
        {
            var pw = new PlanWarning { RawWarning = raw };

            if (raw.Contains("NoJoinPredicate"))
            {
                pw.Type = WarningType.MissingJoinPredicate;
                pw.Severity = WarningSeverity.Critical;
                pw.Description = "Missing JOIN predicate — Cartesian product detected";
                pw.Recommendation = "Add proper JOIN condition to avoid Cartesian product";
            }
            else if (raw.Contains("ColumnsWithNoStatistics") || raw.Contains("Missing stats on:"))
            {
                pw.Type = WarningType.MissingStatistics;
                pw.Severity = WarningSeverity.High;
                pw.Description = $"Missing statistics: {raw}";
                pw.Recommendation = "Run UPDATE STATISTICS on affected columns";
            }
            else if (raw.Contains("SpillToTempDb"))
            {
                pw.Type = WarningType.SpillToTempDb;
                pw.Severity = WarningSeverity.High;
                pw.Description = "Sort/Hash spilling to tempdb";
                pw.Recommendation = "Increase memory grant or optimize sort operations";
            }
            else if (raw.Contains("ImplicitConversion") || raw.Contains("CONVERT_IMPLICIT"))
            {
                pw.Type = WarningType.ImplicitConversion;
                pw.Severity = WarningSeverity.High;
                pw.Description = $"Implicit type conversion: {raw}";
                pw.Recommendation = "Ensure data types match to allow index seek";
            }
            else if (raw.Contains("UnmatchedIndexes"))
            {
                pw.Type = WarningType.UnmatchedIndexes;
                pw.Severity = WarningSeverity.Medium;
                pw.Description = raw;
                pw.Recommendation = "Review index usage and consider dropping unused indexes";
            }
            else
            {
                pw.Type = WarningType.Other;
                pw.Severity = WarningSeverity.Info;
                pw.Description = raw;
                pw.Recommendation = "Review execution plan for details";
            }

            result.Add(pw);
        }

        return result;
    }

    /// <summary>
    /// Build index recommendations from missing indexes.
    /// </summary>
    private List<IndexRecommendation> BuildIndexRecommendations(List<MissingIndex> missingIndexes)
    {
        var recommendations = new List<IndexRecommendation>();

        foreach (var mi in missingIndexes)
        {
            var rec = new IndexRecommendation
            {
                TableName = $"{mi.Schema}.{mi.Table}",
                KeyColumns = mi.EqualityColumns.Concat(mi.InequalityColumns).ToList(),
                IncludeColumns = mi.IncludedColumns,
                ImpactPercentage = mi.Impact,
                CreateStatement = mi.GenerateCreateStatement()
            };

            recommendations.Add(rec);
        }

        return recommendations.OrderByDescending(r => r.ImpactPercentage).ToList();
    }

    /// <summary>
    /// Detect implicit conversions from execution plan.
    /// </summary>
    private List<ImplicitConversion> DetectImplicitConversions(ExecutionPlan plan)
    {
        var conversions = new List<ImplicitConversion>();

        foreach (var warning in plan.Warnings.Where(w => w.Contains("CONVERT_IMPLICIT")))
        {
            // Parse CONVERT_IMPLICIT(type,column,style) from warning
            // Example: "CONVERT_IMPLICIT(nvarchar(100),[column],0)"
            var conversion = new ImplicitConversion
            {
                ColumnName = ExtractColumnFromConversion(warning),
                FromType = "Unknown",
                ToType = ExtractTypeFromConversion(warning),
                Impact = "May prevent index usage and cause performance degradation"
            };

            conversions.Add(conversion);
        }

        return conversions;
    }

    /// <summary>
    /// Extract column name from CONVERT_IMPLICIT warning.
    /// </summary>
    private string ExtractColumnFromConversion(string warning)
    {
        // Simple extraction - in production would use regex
        var start = warning.IndexOf('[');
        var end = warning.IndexOf(']');
        if (start >= 0 && end > start)
        {
            return warning.Substring(start + 1, end - start - 1);
        }
        return "Unknown";
    }

    /// <summary>
    /// Extract target type from CONVERT_IMPLICIT warning.
    /// </summary>
    private string ExtractTypeFromConversion(string warning)
    {
        // Simple extraction - in production would use regex
        var start = warning.IndexOf('(');
        var end = warning.IndexOf(',');
        if (start >= 0 && end > start)
        {
            return warning.Substring(start + 1, end - start - 1);
        }
        return "Unknown";
    }

    /// <summary>
    /// Extract missing statistics from execution plan warnings.
    /// </summary>
    private List<string> ExtractMissingStatistics(ExecutionPlan plan)
    {
        var missingStats = new List<string>();

        foreach (var warning in plan.Warnings)
        {
            if (warning.Contains("Missing stats on:"))
            {
                var stats = warning.Replace("Missing stats on:", "").Trim();
                missingStats.AddRange(stats.Split(',').Select(s => s.Trim()));
            }
        }

        return missingStats.Distinct().ToList();
    }

    /// <summary>
    /// Determine if query needs optimization based on execution plan.
    /// </summary>
    private bool DetermineIfOptimizationNeeded(ExecutionPlan plan)
    {
        // Low cost and no warnings = already optimal
        if (plan.EstimatedTotalCost < 0.1 && !plan.Warnings.Any())
            return false;

        // Has warnings = needs optimization
        if (plan.Warnings.Any())
            return true;

        // High impact missing indexes = needs optimization
        if (plan.MissingIndexes.Any(m => m.Impact > 10))
            return true;

        // Expensive scans = needs optimization
        var hasExpensiveScans = plan.Operators.Any(op =>
            (op.Type.Contains("Scan") || op.Type == "Table Scan") && op.EstimatedCost > 1.0);

        return hasExpensiveScans;
    }
}

/// <summary>
/// Represents a parsed SQL Server execution plan.
/// </summary>
public class ExecutionPlan
{
    public double EstimatedTotalCost { get; set; }
    public long EstimatedRows { get; set; }
    public List<PlanOperator> Operators { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<MissingIndex> MissingIndexes { get; set; } = new();
}

/// <summary>
/// Represents a single operator in an execution plan.
/// </summary>
public class PlanOperator
{
    public string Type { get; set; } = string.Empty;
    public string LogicalOp { get; set; } = string.Empty;
    public double EstimatedCost { get; set; }
    public double EstimatedRows { get; set; }
    public double EstimatedCPU { get; set; }
    public double EstimatedIO { get; set; }
    public string? ObjectName { get; set; }
    public string? IndexName { get; set; }
}

/// <summary>
/// Comparison result between original and optimized execution plans.
/// </summary>
public class PlanComparison
{
    public double OriginalCost { get; set; }
    public double OptimizedCost { get; set; }
    public double ImprovementFactor { get; set; }
    public double ImprovementPercentage { get; set; }
    public bool IsImproved { get; set; }
    public string ImprovementDescription { get; set; } = string.Empty;
    public List<PlanOperator> OriginalOperators { get; set; } = new();
    public List<PlanOperator> OptimizedOperators { get; set; } = new();
    public List<string> OriginalWarnings { get; set; } = new();
    public List<string> OptimizedWarnings { get; set; } = new();
}
