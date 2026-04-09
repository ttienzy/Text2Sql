using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Analyzes naming conventions in database schema
/// Detects patterns, inconsistencies, and suggests standardization
/// </summary>
public class NamingConventionAnalyzer
{
    private readonly ILogger<NamingConventionAnalyzer> _logger;

    public NamingConventionAnalyzer(ILogger<NamingConventionAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze naming conventions across the entire schema
    /// </summary>
    public NamingConventionReport AnalyzeSchema(EnhancedDatabaseSchema schema)
    {
        _logger.LogInformation("[NamingConventionAnalyzer] Analyzing naming conventions for {Tables} tables",
            schema.EnhancedTables.Count);

        var report = new NamingConventionReport
        {
            AnalyzedAt = DateTime.UtcNow,
            TotalTables = schema.EnhancedTables.Count,
            TotalColumns = schema.EnhancedTables.Sum(t => t.ColumnCount)
        };

        // Analyze table naming patterns
        var tablePatterns = AnalyzeTableNames(schema.EnhancedTables);
        report.DominantTablePattern = tablePatterns.DominantPattern;
        report.TablePatternStatistics = tablePatterns.Statistics;

        // Analyze column naming patterns
        var columnPatterns = AnalyzeColumnNames(schema.EnhancedTables);
        report.DominantColumnPattern = columnPatterns.DominantPattern;
        report.ColumnPatternStatistics = columnPatterns.Statistics;

        // Detect inconsistencies
        report.Inconsistencies = DetectInconsistencies(schema.EnhancedTables, tablePatterns, columnPatterns);

        // Generate recommendations
        report.Recommendations = GenerateRecommendations(report);

        _logger.LogInformation(
            "[NamingConventionAnalyzer] ✅ Analysis complete: Dominant={Dominant}, Inconsistencies={Count}",
            report.DominantTablePattern,
            report.Inconsistencies.Count);

        return report;
    }

    /// <summary>
    /// Analyze table naming patterns
    /// </summary>
    private (NamingPattern DominantPattern, Dictionary<string, int> Statistics) AnalyzeTableNames(
        List<EnhancedTableInfo> tables)
    {
        var statistics = new Dictionary<string, int>
        {
            ["PascalCase"] = 0,
            ["snake_case"] = 0,
            ["camelCase"] = 0,
            ["UPPER_CASE"] = 0,
            ["Mixed"] = 0
        };

        foreach (var table in tables)
        {
            var pattern = DetectNamingPattern(table.TableName);
            statistics[pattern.ToString()]++;
        }

        // Find dominant pattern
        var dominant = statistics.OrderByDescending(kvp => kvp.Value).First();
        var dominantPattern = Enum.Parse<NamingPattern>(dominant.Key);

        return (dominantPattern, statistics);
    }

    /// <summary>
    /// Analyze column naming patterns
    /// </summary>
    private (NamingPattern DominantPattern, Dictionary<string, int> Statistics) AnalyzeColumnNames(
        List<EnhancedTableInfo> tables)
    {
        var statistics = new Dictionary<string, int>
        {
            ["PascalCase"] = 0,
            ["snake_case"] = 0,
            ["camelCase"] = 0,
            ["UPPER_CASE"] = 0,
            ["Mixed"] = 0
        };

        foreach (var table in tables)
        {
            foreach (var column in table.Columns)
            {
                var pattern = DetectNamingPattern(column.ColumnName);
                statistics[pattern.ToString()]++;
            }
        }

        // Find dominant pattern
        var dominant = statistics.OrderByDescending(kvp => kvp.Value).First();
        var dominantPattern = Enum.Parse<NamingPattern>(dominant.Key);

        return (dominantPattern, statistics);
    }

    /// <summary>
    /// Detect naming pattern for a single identifier
    /// </summary>
    private NamingPattern DetectNamingPattern(string name)
    {
        // PascalCase: Starts with uppercase, has uppercase letters (e.g., CustomerOrder, OrderId)
        if (Regex.IsMatch(name, @"^[A-Z][a-z]+([A-Z][a-z]*)*$"))
            return NamingPattern.PascalCase;

        // camelCase: Starts with lowercase, has uppercase letters (e.g., customerId, orderDate)
        if (Regex.IsMatch(name, @"^[a-z]+([A-Z][a-z]*)+$"))
            return NamingPattern.camelCase;

        // snake_case: All lowercase with underscores (e.g., customer_id, order_date)
        if (Regex.IsMatch(name, @"^[a-z]+(_[a-z]+)*$"))
            return NamingPattern.snake_case;

        // UPPER_CASE: All uppercase with underscores (e.g., CUSTOMER_ID, ORDER_DATE)
        if (Regex.IsMatch(name, @"^[A-Z]+(_[A-Z]+)*$"))
            return NamingPattern.UPPER_CASE;

        // Mixed or unclear pattern
        return NamingPattern.Mixed;
    }

    /// <summary>
    /// Detect inconsistencies in naming conventions
    /// </summary>
    private List<NamingInconsistency> DetectInconsistencies(
        List<EnhancedTableInfo> tables,
        (NamingPattern DominantPattern, Dictionary<string, int> Statistics) tablePatterns,
        (NamingPattern DominantPattern, Dictionary<string, int> Statistics) columnPatterns)
    {
        var inconsistencies = new List<NamingInconsistency>();

        // Check table naming inconsistencies
        foreach (var table in tables)
        {
            var pattern = DetectNamingPattern(table.TableName);
            if (pattern != tablePatterns.DominantPattern && pattern != NamingPattern.Mixed)
            {
                inconsistencies.Add(new NamingInconsistency
                {
                    Type = InconsistencyType.TableNaming,
                    Table = table.TableName,
                    CurrentName = table.TableName,
                    CurrentPattern = pattern,
                    ExpectedPattern = tablePatterns.DominantPattern,
                    SuggestedName = ConvertNamingPattern(table.TableName, pattern, tablePatterns.DominantPattern),
                    Severity = InconsistencySeverity.Warning,
                    Description = $"Table '{table.TableName}' uses {pattern} but schema predominantly uses {tablePatterns.DominantPattern}"
                });
            }

            // Check column naming inconsistencies
            foreach (var column in table.Columns)
            {
                var colPattern = DetectNamingPattern(column.ColumnName);
                if (colPattern != columnPatterns.DominantPattern && colPattern != NamingPattern.Mixed)
                {
                    inconsistencies.Add(new NamingInconsistency
                    {
                        Type = InconsistencyType.ColumnNaming,
                        Table = table.TableName,
                        Column = column.ColumnName,
                        CurrentName = column.ColumnName,
                        CurrentPattern = colPattern,
                        ExpectedPattern = columnPatterns.DominantPattern,
                        SuggestedName = ConvertNamingPattern(column.ColumnName, colPattern, columnPatterns.DominantPattern),
                        Severity = InconsistencySeverity.Info,
                        Description = $"Column '{table.TableName}.{column.ColumnName}' uses {colPattern} but schema predominantly uses {columnPatterns.DominantPattern}"
                    });
                }
            }
        }

        // Detect similar table names (potential duplicates or typos)
        DetectSimilarNames(tables, inconsistencies);

        return inconsistencies;
    }

    /// <summary>
    /// Detect similar table names that might be duplicates or typos
    /// </summary>
    private void DetectSimilarNames(List<EnhancedTableInfo> tables, List<NamingInconsistency> inconsistencies)
    {
        var tableNames = tables.Select(t => t.TableName).ToList();

        for (int i = 0; i < tableNames.Count; i++)
        {
            for (int j = i + 1; j < tableNames.Count; j++)
            {
                var name1 = tableNames[i].ToLower();
                var name2 = tableNames[j].ToLower();

                // Check if names are very similar (Levenshtein distance)
                var distance = CalculateLevenshteinDistance(name1, name2);
                var maxLength = Math.Max(name1.Length, name2.Length);
                var similarity = 1.0 - (double)distance / maxLength;

                // If similarity > 80%, flag as potential duplicate
                if (similarity > 0.8 && similarity < 1.0)
                {
                    inconsistencies.Add(new NamingInconsistency
                    {
                        Type = InconsistencyType.SimilarNames,
                        Table = tableNames[i],
                        CurrentName = tableNames[i],
                        SuggestedName = tableNames[j],
                        Severity = InconsistencySeverity.Warning,
                        Description = $"Tables '{tableNames[i]}' and '{tableNames[j]}' have similar names ({similarity:P0} similarity). Potential duplicate or typo?"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Calculate Levenshtein distance between two strings
    /// </summary>
    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        var d = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            d[0, j] = j;

        for (int j = 1; j <= s2.Length; j++)
        {
            for (int i = 1; i <= s1.Length; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s1.Length, s2.Length];
    }

    /// <summary>
    /// Convert naming pattern from one style to another
    /// </summary>
    private string ConvertNamingPattern(string name, NamingPattern from, NamingPattern to)
    {
        // Split name into words
        var words = SplitIntoWords(name, from);

        // Convert to target pattern
        return to switch
        {
            NamingPattern.PascalCase => string.Join("", words.Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower())),
            NamingPattern.camelCase => string.Join("", words.Select((w, i) => i == 0 ? w.ToLower() : char.ToUpper(w[0]) + w.Substring(1).ToLower())),
            NamingPattern.snake_case => string.Join("_", words.Select(w => w.ToLower())),
            NamingPattern.UPPER_CASE => string.Join("_", words.Select(w => w.ToUpper())),
            _ => name
        };
    }

    /// <summary>
    /// Split identifier into words based on current pattern
    /// </summary>
    private List<string> SplitIntoWords(string name, NamingPattern pattern)
    {
        return pattern switch
        {
            NamingPattern.PascalCase or NamingPattern.camelCase =>
                Regex.Split(name, @"(?<!^)(?=[A-Z])").ToList(),
            NamingPattern.snake_case or NamingPattern.UPPER_CASE =>
                name.Split('_').ToList(),
            _ => new List<string> { name }
        };
    }

    /// <summary>
    /// Generate recommendations for standardization
    /// </summary>
    private List<NamingRecommendation> GenerateRecommendations(NamingConventionReport report)
    {
        var recommendations = new List<NamingRecommendation>();

        // Recommendation 1: Standardize to dominant pattern
        if (report.Inconsistencies.Any(i => i.Type == InconsistencyType.TableNaming))
        {
            var tableInconsistencies = report.Inconsistencies
                .Where(i => i.Type == InconsistencyType.TableNaming)
                .ToList();

            recommendations.Add(new NamingRecommendation
            {
                Title = $"Standardize table names to {report.DominantTablePattern}",
                Description = $"Found {tableInconsistencies.Count} tables not following the dominant {report.DominantTablePattern} pattern",
                Priority = RecommendationPriority.Medium,
                AffectedTables = tableInconsistencies.Select(i => i.Table!).Distinct().ToList(),
                SqlScript = GenerateBulkRenameScript(tableInconsistencies, "TABLE")
            });
        }

        // Recommendation 2: Standardize column names
        if (report.Inconsistencies.Any(i => i.Type == InconsistencyType.ColumnNaming))
        {
            var columnInconsistencies = report.Inconsistencies
                .Where(i => i.Type == InconsistencyType.ColumnNaming)
                .ToList();

            recommendations.Add(new NamingRecommendation
            {
                Title = $"Standardize column names to {report.DominantColumnPattern}",
                Description = $"Found {columnInconsistencies.Count} columns not following the dominant {report.DominantColumnPattern} pattern",
                Priority = RecommendationPriority.Low,
                AffectedTables = columnInconsistencies.Select(i => i.Table!).Distinct().ToList(),
                SqlScript = GenerateBulkRenameScript(columnInconsistencies, "COLUMN")
            });
        }

        // Recommendation 3: Review similar names
        if (report.Inconsistencies.Any(i => i.Type == InconsistencyType.SimilarNames))
        {
            var similarNames = report.Inconsistencies
                .Where(i => i.Type == InconsistencyType.SimilarNames)
                .ToList();

            recommendations.Add(new NamingRecommendation
            {
                Title = "Review similar table names",
                Description = $"Found {similarNames.Count} pairs of tables with similar names. Review for potential duplicates or typos.",
                Priority = RecommendationPriority.High,
                AffectedTables = similarNames.Select(i => i.Table!).Distinct().ToList(),
                SqlScript = "-- Manual review required for similar names"
            });
        }

        return recommendations;
    }

    /// <summary>
    /// Generate SQL script for bulk rename operations
    /// </summary>
    private string GenerateBulkRenameScript(List<NamingInconsistency> inconsistencies, string objectType)
    {
        var script = new System.Text.StringBuilder();
        script.AppendLine("-- Bulk Rename Script");
        script.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        script.AppendLine($"-- Object Type: {objectType}");
        script.AppendLine();
        script.AppendLine("-- WARNING: Review and test in non-production environment first!");
        script.AppendLine("-- This script will rename database objects.");
        script.AppendLine();

        foreach (var inconsistency in inconsistencies.Take(50)) // Limit to 50 for safety
        {
            if (objectType == "TABLE")
            {
                script.AppendLine($"-- Rename table: {inconsistency.CurrentName} → {inconsistency.SuggestedName}");
                script.AppendLine($"EXEC sp_rename '{inconsistency.CurrentName}', '{inconsistency.SuggestedName}';");
                script.AppendLine();
            }
            else if (objectType == "COLUMN")
            {
                script.AppendLine($"-- Rename column: {inconsistency.Table}.{inconsistency.CurrentName} → {inconsistency.SuggestedName}");
                script.AppendLine($"EXEC sp_rename '{inconsistency.Table}.{inconsistency.CurrentName}', '{inconsistency.SuggestedName}', 'COLUMN';");
                script.AppendLine();
            }
        }

        if (inconsistencies.Count > 50)
        {
            script.AppendLine($"-- ... and {inconsistencies.Count - 50} more renames");
        }

        return script.ToString();
    }
}

/// <summary>
/// Naming convention analysis report
/// </summary>
public class NamingConventionReport
{
    public DateTime AnalyzedAt { get; set; }
    public int TotalTables { get; set; }
    public int TotalColumns { get; set; }
    public NamingPattern DominantTablePattern { get; set; }
    public NamingPattern DominantColumnPattern { get; set; }
    public Dictionary<string, int> TablePatternStatistics { get; set; } = new();
    public Dictionary<string, int> ColumnPatternStatistics { get; set; } = new();
    public List<NamingInconsistency> Inconsistencies { get; set; } = new();
    public List<NamingRecommendation> Recommendations { get; set; } = new();
}

/// <summary>
/// Naming pattern types
/// </summary>
public enum NamingPattern
{
    PascalCase,
    camelCase,
    snake_case,
    UPPER_CASE,
    Mixed
}

/// <summary>
/// Naming inconsistency
/// </summary>
public class NamingInconsistency
{
    public InconsistencyType Type { get; set; }
    public string? Table { get; set; }
    public string? Column { get; set; }
    public string CurrentName { get; set; } = string.Empty;
    public string? SuggestedName { get; set; }
    public NamingPattern CurrentPattern { get; set; }
    public NamingPattern ExpectedPattern { get; set; }
    public InconsistencySeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Inconsistency type
/// </summary>
public enum InconsistencyType
{
    TableNaming,
    ColumnNaming,
    SimilarNames
}

/// <summary>
/// Inconsistency severity
/// </summary>
public enum InconsistencySeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Naming recommendation
/// </summary>
public class NamingRecommendation
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RecommendationPriority Priority { get; set; }
    public List<string> AffectedTables { get; set; } = new();
    public string SqlScript { get; set; } = string.Empty;
}

/// <summary>
/// Recommendation priority
/// </summary>
public enum RecommendationPriority
{
    Low,
    Medium,
    High
}
