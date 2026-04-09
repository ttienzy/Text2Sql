using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// AI-powered index recommendation engine
/// Detects missing indexes, unused indexes, and calculates impact scores
/// </summary>
public class IndexRecommendationEngine
{
    private readonly ILogger<IndexRecommendationEngine> _logger;

    public IndexRecommendationEngine(ILogger<IndexRecommendationEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze indexes and generate recommendations
    /// </summary>
    public IndexRecommendationReport AnalyzeIndexes(EnhancedDatabaseSchema schema)
    {
        _logger.LogInformation("[IndexRecommendationEngine] Analyzing indexes for {Tables} tables",
            schema.EnhancedTables.Count);

        var report = new IndexRecommendationReport
        {
            AnalyzedAt = DateTime.UtcNow,
            TotalTables = schema.EnhancedTables.Count,
            TotalIndexes = schema.EnhancedTables.Sum(t => t.Indexes.Count)
        };

        // Detect missing indexes on foreign keys
        var missingFkIndexes = DetectMissingForeignKeyIndexes(schema);
        report.Recommendations.AddRange(missingFkIndexes);

        // Detect missing indexes on frequently filtered columns
        var missingFilterIndexes = DetectMissingFilterIndexes(schema);
        report.Recommendations.AddRange(missingFilterIndexes);

        // Detect potential composite indexes
        var compositeIndexes = DetectCompositeIndexOpportunities(schema);
        report.Recommendations.AddRange(compositeIndexes);

        // Detect redundant indexes
        var redundantIndexes = DetectRedundantIndexes(schema);
        report.Recommendations.AddRange(redundantIndexes);

        // Detect covering index opportunities
        var coveringIndexes = DetectCoveringIndexOpportunities(schema);
        report.Recommendations.AddRange(coveringIndexes);

        // Calculate statistics
        report.MissingIndexCount = report.Recommendations.Count(r => r.Type == IndexRecommendationType.Create);
        report.RedundantIndexCount = report.Recommendations.Count(r => r.Type == IndexRecommendationType.Drop);
        report.OptimizationCount = report.Recommendations.Count(r => r.Type == IndexRecommendationType.Optimize);

        _logger.LogInformation(
            "[IndexRecommendationEngine] ✅ Analysis complete: {Missing} missing, {Redundant} redundant, {Optimize} optimizations",
            report.MissingIndexCount,
            report.RedundantIndexCount,
            report.OptimizationCount);

        return report;
    }

    /// <summary>
    /// Detect missing indexes on foreign key columns
    /// </summary>
    private List<IndexRecommendation> DetectMissingForeignKeyIndexes(EnhancedDatabaseSchema schema)
    {
        var recommendations = new List<IndexRecommendation>();

        foreach (var table in schema.EnhancedTables)
        {
            // Get FK columns
            var fkColumns = table.Columns
                .Where(c => c.IsForeignKey)
                .ToList();

            foreach (var fkColumn in fkColumns)
            {
                // Check if there's an index on this FK column
                var hasIndex = table.Indexes.Any(idx =>
                    idx.Columns.Count == 1 && idx.Columns[0] == fkColumn.ColumnName);

                if (!hasIndex)
                {
                    // Find the referenced table
                    var relationship = schema.BaseSchema.Relationships
                        .FirstOrDefault(r => r.FromTable == table.TableName && r.FromColumn == fkColumn.ColumnName);

                    var referencedTable = relationship?.ToTable ?? "Unknown";

                    recommendations.Add(new IndexRecommendation
                    {
                        Type = IndexRecommendationType.Create,
                        Table = table.TableName,
                        Columns = new List<string> { fkColumn.ColumnName },
                        IndexName = $"IX_{table.TableName}_{fkColumn.ColumnName}",
                        Reason = $"Foreign key column without index. References {referencedTable}.",
                        Impact = CalculateImpact(table, new[] { fkColumn.ColumnName }, ImpactFactor.ForeignKey),
                        EstimatedImprovement = "30-50% faster JOIN queries",
                        SqlScript = GenerateCreateIndexScript(table.TableName, $"IX_{table.TableName}_{fkColumn.ColumnName}", new[] { fkColumn.ColumnName }, false)
                    });
                }
            }
        }

        return recommendations;
    }

    /// <summary>
    /// Detect missing indexes on frequently filtered columns
    /// </summary>
    private List<IndexRecommendation> DetectMissingFilterIndexes(EnhancedDatabaseSchema schema)
    {
        var recommendations = new List<IndexRecommendation>();

        foreach (var table in schema.EnhancedTables)
        {
            // Heuristic: Columns with specific names are often used in WHERE clauses
            var filterCandidates = table.Columns
                .Where(c => !c.IsPrimaryKey && !c.IsForeignKey)
                .Where(c =>
                    c.ColumnName.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                    c.ColumnName.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
                    c.ColumnName.Contains("Type", StringComparison.OrdinalIgnoreCase) ||
                    c.ColumnName.Contains("Category", StringComparison.OrdinalIgnoreCase) ||
                    c.ColumnName.Contains("State", StringComparison.OrdinalIgnoreCase) ||
                    c.ColumnName.Contains("Active", StringComparison.OrdinalIgnoreCase) ||
                    c.ColumnName.Contains("Enabled", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var column in filterCandidates)
            {
                // Check if there's already an index on this column
                var hasIndex = table.Indexes.Any(idx =>
                    idx.Columns.Contains(column.ColumnName));

                if (!hasIndex && table.RowCount > 10000) // Only recommend for tables with significant data
                {
                    recommendations.Add(new IndexRecommendation
                    {
                        Type = IndexRecommendationType.Create,
                        Table = table.TableName,
                        Columns = new List<string> { column.ColumnName },
                        IndexName = $"IX_{table.TableName}_{column.ColumnName}",
                        Reason = $"Frequently filtered column ({column.ColumnName}) without index on large table ({table.RowCount:N0} rows).",
                        Impact = CalculateImpact(table, new[] { column.ColumnName }, ImpactFactor.FrequentFilter),
                        EstimatedImprovement = "20-40% faster WHERE clause queries",
                        SqlScript = GenerateCreateIndexScript(table.TableName, $"IX_{table.TableName}_{column.ColumnName}", new[] { column.ColumnName }, false)
                    });
                }
            }
        }

        return recommendations;
    }

    /// <summary>
    /// Detect composite index opportunities
    /// </summary>
    private List<IndexRecommendation> DetectCompositeIndexOpportunities(EnhancedDatabaseSchema schema)
    {
        var recommendations = new List<IndexRecommendation>();

        foreach (var table in schema.EnhancedTables)
        {
            // Look for FK + Date column combinations (common query pattern)
            var fkColumns = table.Columns.Where(c => c.IsForeignKey).ToList();
            var dateColumns = table.Columns
                .Where(c => c.DataType.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                           c.DataType.Contains("time", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var fk in fkColumns)
            {
                foreach (var date in dateColumns)
                {
                    // Check if there's already a composite index
                    var hasCompositeIndex = table.Indexes.Any(idx =>
                        idx.Columns.Contains(fk.ColumnName) && idx.Columns.Contains(date.ColumnName));

                    if (!hasCompositeIndex && table.RowCount > 50000)
                    {
                        recommendations.Add(new IndexRecommendation
                        {
                            Type = IndexRecommendationType.Create,
                            Table = table.TableName,
                            Columns = new List<string> { fk.ColumnName, date.ColumnName },
                            IndexName = $"IX_{table.TableName}_{fk.ColumnName}_{date.ColumnName}",
                            Reason = $"Composite index opportunity: FK + Date column on large table ({table.RowCount:N0} rows).",
                            Impact = CalculateImpact(table, new[] { fk.ColumnName, date.ColumnName }, ImpactFactor.Composite),
                            EstimatedImprovement = "40-60% faster filtered JOIN queries",
                            SqlScript = GenerateCreateIndexScript(table.TableName, $"IX_{table.TableName}_{fk.ColumnName}_{date.ColumnName}", new[] { fk.ColumnName, date.ColumnName }, false)
                        });
                    }
                }
            }
        }

        return recommendations.Take(10).ToList(); // Limit to top 10 composite recommendations
    }

    /// <summary>
    /// Detect redundant indexes
    /// </summary>
    private List<IndexRecommendation> DetectRedundantIndexes(EnhancedDatabaseSchema schema)
    {
        var recommendations = new List<IndexRecommendation>();

        foreach (var table in schema.EnhancedTables)
        {
            var indexes = table.Indexes.Where(idx => !idx.IsPrimaryKey).ToList();

            for (int i = 0; i < indexes.Count; i++)
            {
                for (int j = i + 1; j < indexes.Count; j++)
                {
                    var idx1 = indexes[i];
                    var idx2 = indexes[j];

                    // Check if idx1 is a prefix of idx2 (redundant)
                    if (IsIndexPrefix(idx1.Columns, idx2.Columns))
                    {
                        recommendations.Add(new IndexRecommendation
                        {
                            Type = IndexRecommendationType.Drop,
                            Table = table.TableName,
                            Columns = idx1.Columns,
                            IndexName = idx1.IndexName,
                            Reason = $"Redundant index. Covered by {idx2.IndexName} ({string.Join(", ", idx2.Columns)}).",
                            Impact = IndexImpact.Low,
                            EstimatedImprovement = "Reduced storage and maintenance overhead",
                            SqlScript = GenerateDropIndexScript(table.TableName, idx1.IndexName)
                        });
                    }
                }
            }
        }

        return recommendations;
    }

    /// <summary>
    /// Detect covering index opportunities
    /// </summary>
    private List<IndexRecommendation> DetectCoveringIndexOpportunities(EnhancedDatabaseSchema schema)
    {
        var recommendations = new List<IndexRecommendation>();

        foreach (var table in schema.EnhancedTables)
        {
            // Look for indexes that could benefit from INCLUDE columns
            var nonClusteredIndexes = table.Indexes
                .Where(idx => !idx.IsPrimaryKey && idx.Columns.Count <= 2)
                .ToList();

            foreach (var index in nonClusteredIndexes)
            {
                // Suggest adding frequently selected columns to INCLUDE
                var includeColumns = table.Columns
                    .Where(c => !index.Columns.Contains(c.ColumnName) && !c.IsPrimaryKey)
                    .Where(c => c.DataType.Contains("varchar", StringComparison.OrdinalIgnoreCase) ||
                               c.DataType.Contains("int", StringComparison.OrdinalIgnoreCase) ||
                               c.DataType.Contains("decimal", StringComparison.OrdinalIgnoreCase))
                    .Take(3) // Limit to 3 INCLUDE columns
                    .Select(c => c.ColumnName)
                    .ToList();

                if (includeColumns.Any() && table.RowCount > 100000)
                {
                    recommendations.Add(new IndexRecommendation
                    {
                        Type = IndexRecommendationType.Optimize,
                        Table = table.TableName,
                        Columns = index.Columns,
                        IndexName = index.IndexName,
                        Reason = $"Covering index opportunity. Add INCLUDE columns to avoid key lookups.",
                        Impact = CalculateImpact(table, index.Columns.ToArray(), ImpactFactor.Covering),
                        EstimatedImprovement = "10-30% faster SELECT queries",
                        SqlScript = GenerateCreateIndexScript(table.TableName, index.IndexName, index.Columns.ToArray(), false, includeColumns.ToArray())
                    });
                }
            }
        }

        return recommendations.Take(5).ToList(); // Limit to top 5 covering index recommendations
    }

    /// <summary>
    /// Check if columns1 is a prefix of columns2
    /// </summary>
    private bool IsIndexPrefix(List<string> columns1, List<string> columns2)
    {
        if (columns1.Count >= columns2.Count)
            return false;

        for (int i = 0; i < columns1.Count; i++)
        {
            if (columns1[i] != columns2[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Calculate impact score
    /// </summary>
    private IndexImpact CalculateImpact(EnhancedTableInfo table, string[] columns, ImpactFactor factor)
    {
        var score = 0;

        // Row count factor (more rows = higher impact)
        if (table.RowCount > 1000000) score += 30;
        else if (table.RowCount > 100000) score += 20;
        else if (table.RowCount > 10000) score += 10;

        // Factor-specific scoring
        score += factor switch
        {
            ImpactFactor.ForeignKey => 40, // High impact for FK indexes
            ImpactFactor.FrequentFilter => 30,
            ImpactFactor.Composite => 35,
            ImpactFactor.Covering => 25,
            _ => 10
        };

        // Column selectivity (fewer distinct values = lower impact)
        foreach (var colName in columns)
        {
            if (table.ColumnStats.TryGetValue(colName, out var stats))
            {
                var selectivity = (double)stats.DistinctCount / table.RowCount;
                if (selectivity > 0.5) score += 10; // High selectivity
                else if (selectivity > 0.1) score += 5;
            }
        }

        // Convert score to impact level
        if (score >= 70) return IndexImpact.High;
        if (score >= 40) return IndexImpact.Medium;
        return IndexImpact.Low;
    }

    /// <summary>
    /// Generate CREATE INDEX script
    /// </summary>
    private string GenerateCreateIndexScript(string tableName, string indexName, string[] columns, bool unique, string[]? includeColumns = null)
    {
        var script = $"CREATE {(unique ? "UNIQUE " : "")}NONCLUSTERED INDEX [{indexName}]\n";
        script += $"ON [dbo].[{tableName}] ({string.Join(", ", columns.Select(c => $"[{c}]"))})\n";

        if (includeColumns?.Any() == true)
        {
            script += $"INCLUDE ({string.Join(", ", includeColumns.Select(c => $"[{c}]"))})\n";
        }

        script += "WITH (ONLINE = ON, FILLFACTOR = 90);";

        return script;
    }

    /// <summary>
    /// Generate DROP INDEX script
    /// </summary>
    private string GenerateDropIndexScript(string tableName, string indexName)
    {
        return $"DROP INDEX [{indexName}] ON [dbo].[{tableName}];";
    }
}

/// <summary>
/// Index recommendation report
/// </summary>
public class IndexRecommendationReport
{
    public DateTime AnalyzedAt { get; set; }
    public int TotalTables { get; set; }
    public int TotalIndexes { get; set; }
    public int MissingIndexCount { get; set; }
    public int RedundantIndexCount { get; set; }
    public int OptimizationCount { get; set; }
    public List<IndexRecommendation> Recommendations { get; set; } = new();
}

/// <summary>
/// Index recommendation
/// </summary>
public class IndexRecommendation
{
    public IndexRecommendationType Type { get; set; }
    public string Table { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public string IndexName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public IndexImpact Impact { get; set; }
    public string EstimatedImprovement { get; set; } = string.Empty;
    public string SqlScript { get; set; } = string.Empty;
}

/// <summary>
/// Index recommendation type
/// </summary>
public enum IndexRecommendationType
{
    Create,
    Drop,
    Optimize
}

/// <summary>
/// Index impact level
/// </summary>
public enum IndexImpact
{
    Low,
    Medium,
    High
}

/// <summary>
/// Impact factor for scoring
/// </summary>
internal enum ImpactFactor
{
    ForeignKey,
    FrequentFilter,
    Composite,
    Covering
}
