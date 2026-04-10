using System.Collections.Generic;
using System.Linq;
using System.Text;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Manages token budget for LLM context to prevent exceeding model limits.
/// Prioritizes critical information and truncates lower-priority sections.
/// </summary>
public class ContextBudgetManager
{
    private const int MaxContextTokens = 6000;
    private const int CharsPerToken = 4; // Rough estimate for token counting

    /// <summary>
    /// Build prioritized context sections within token budget.
    /// Priority 1-4: Always included (truncated if needed)
    /// Priority 5+: Included only if budget allows
    /// </summary>
    public string BuildPrioritizedContext(
        PreFlightAnalysis preFlight,
        Dictionary<string, ColumnStatistics> columnStats,
        List<AntiPattern> issues,
        SchemaContext schema)
    {
        // Build sections with priority ordering
        var sections = new List<ContextSection>
        {
            new()
            {
                Priority = 1,
                Name = "CRITICAL WARNINGS",
                Content = BuildCriticalWarningsText(preFlight.Warnings
                    .Where(w => w.Severity == WarningSeverity.Critical)
                    .ToList())
            },

            new()
            {
                Priority = 2,
                Name = "TOP COST DRIVERS",
                Content = BuildCostDriversText(preFlight.CostDrivers)
            },

            new()
            {
                Priority = 3,
                Name = "CRITICAL ANTI-PATTERNS",
                Content = BuildIssuesText(issues
                    .Where(i => i.Severity == Severity.Critical || i.Severity == Severity.Error)
                    .ToList())
            },

            new()
            {
                Priority = 4,
                Name = "HIGH SKEW COLUMNS",
                Content = BuildHighSkewStatsText(columnStats
                    .Where(kvp => kvp.Value.SkewFactor > 0.7)
                    .ToDictionary(k => k.Key, v => v.Value))
            },

            new()
            {
                Priority = 5,
                Name = "MISSING INDEX RECOMMENDATIONS",
                Content = BuildMissingIndexesText(preFlight.IndexRecommendations)
            },

            new()
            {
                Priority = 6,
                Name = "ALL WARNINGS",
                Content = BuildWarningsText(preFlight.Warnings
                    .Where(w => w.Severity != WarningSeverity.Critical)
                    .ToList())
            },

            new()
            {
                Priority = 7,
                Name = "ALL ANTI-PATTERNS",
                Content = BuildIssuesText(issues
                    .Where(i => i.Severity != Severity.Critical && i.Severity != Severity.Error)
                    .ToList())
            },

            new()
            {
                Priority = 8,
                Name = "ALL COLUMN STATISTICS",
                Content = BuildAllColumnStatsText(columnStats)
            },

            new()
            {
                Priority = 9,
                Name = "SCHEMA CONTEXT",
                Content = BuildSchemaContextText(schema)
            }
        };

        var result = new StringBuilder();
        var currentTokens = 0;

        foreach (var section in sections.OrderBy(s => s.Priority))
        {
            if (string.IsNullOrWhiteSpace(section.Content)) continue;

            var sectionTokens = EstimateTokens(section.Content);

            if (currentTokens + sectionTokens > MaxContextTokens)
            {
                // Priority 1-4: Must include even if truncated
                if (section.Priority <= 4)
                {
                    var remaining = MaxContextTokens - currentTokens;
                    if (remaining > 100) // Only include if at least 100 tokens available
                    {
                        var truncated = TruncateToTokens(section.Content, remaining);
                        result.AppendLine($"## {section.Name} [TRUNCATED]");
                        result.AppendLine(truncated);
                        result.AppendLine();
                        currentTokens += EstimateTokens(truncated);
                    }
                }
                // Priority 5+: Skip if no budget
                break;
            }
            else
            {
                result.AppendLine($"## {section.Name}");
                result.AppendLine(section.Content);
                result.AppendLine();
                currentTokens += sectionTokens;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Estimate token count from text length.
    /// </summary>
    private int EstimateTokens(string text) => text.Length / CharsPerToken;

    /// <summary>
    /// Truncate text to fit within token budget.
    /// </summary>
    private string TruncateToTokens(string text, int maxTokens)
    {
        var maxChars = maxTokens * CharsPerToken;
        if (text.Length <= maxChars) return text;
        return text[..maxChars] + "\n...[truncated for token budget]";
    }

    /// <summary>
    /// Build text for critical warnings only.
    /// </summary>
    private string BuildCriticalWarningsText(List<PlanWarning> warnings)
    {
        if (!warnings.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var w in warnings)
        {
            lines.Add($"⚠️ CRITICAL: {w.Description}");
            lines.Add($"   Recommendation: {w.Recommendation}");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for cost drivers.
    /// </summary>
    private string BuildCostDriversText(List<CostDriver> costDrivers)
    {
        if (!costDrivers.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var cd in costDrivers.Take(5)) // Top 5 only
        {
            lines.Add($"• {cd.Description}");
            if (!string.IsNullOrEmpty(cd.Recommendation))
            {
                lines.Add($"  {cd.Recommendation}");
            }
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for anti-pattern issues.
    /// </summary>
    private string BuildIssuesText(List<AntiPattern> issues)
    {
        if (!issues.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var issue in issues)
        {
            lines.Add($"[{issue.Code}] {issue.Title}");
            lines.Add($"  Severity: {issue.Severity}");
            lines.Add($"  Description: {issue.Description}");
            lines.Add($"  Impact: {issue.Impact}");
            if (!string.IsNullOrEmpty(issue.AutoFixSuggestion))
            {
                lines.Add($"  Auto-fix: {issue.AutoFixSuggestion}");
            }
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for high skew columns only.
    /// </summary>
    private string BuildHighSkewStatsText(Dictionary<string, ColumnStatistics> stats)
    {
        if (!stats.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var kvp in stats.OrderByDescending(x => x.Value.SkewFactor))
        {
            var col = kvp.Key;
            var stat = kvp.Value;

            lines.Add($"Column: {col}");
            lines.Add($"  Skew Factor: {stat.SkewFactor:P2} ({stat.SkewLevel})");
            lines.Add($"  Total Rows: {stat.TotalRows:N0}");
            lines.Add($"  Distinct Values: {stat.DistinctValues:N0}");

            if (stat.TopValues.Any())
            {
                lines.Add("  Top Values:");
                foreach (var tv in stat.TopValues.Take(3))
                {
                    lines.Add($"    '{tv.Value}': {tv.Count:N0} rows ({tv.Percentage}%)");
                }
            }

            if (stat.IsStale)
            {
                lines.Add($"  ⚠️ STALE: {stat.StaleWarning}");
            }

            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for missing index recommendations.
    /// </summary>
    private string BuildMissingIndexesText(List<IndexRecommendation> recommendations)
    {
        if (!recommendations.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var rec in recommendations.Where(r => r.ImpactPercentage > 10).Take(5))
        {
            lines.Add($"Table: {rec.TableName} (Impact: {rec.ImpactPercentage:F2}%)");
            lines.Add($"  Key Columns: {string.Join(", ", rec.KeyColumns)}");
            if (rec.IncludeColumns.Any())
            {
                lines.Add($"  Include Columns: {string.Join(", ", rec.IncludeColumns)}");
            }
            lines.Add($"  {rec.CreateStatement}");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for all warnings (non-critical).
    /// </summary>
    private string BuildWarningsText(List<PlanWarning> warnings)
    {
        if (!warnings.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var w in warnings)
        {
            var icon = w.Severity switch
            {
                WarningSeverity.High => "⚠️",
                WarningSeverity.Medium => "ℹ️",
                _ => "•"
            };
            lines.Add($"{icon} {w.Description}");
            lines.Add($"   {w.Recommendation}");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for all column statistics.
    /// </summary>
    private string BuildAllColumnStatsText(Dictionary<string, ColumnStatistics> stats)
    {
        if (!stats.Any()) return string.Empty;

        var lines = new List<string>();
        foreach (var kvp in stats.OrderByDescending(x => x.Value.SkewFactor))
        {
            var col = kvp.Key;
            var stat = kvp.Value;

            lines.Add($"Column: {col}");
            lines.Add($"  Rows: {stat.TotalRows:N0}, Distinct: {stat.DistinctValues:N0}");
            lines.Add($"  Selectivity: {stat.Selectivity:P2}, Skew: {stat.SkewFactor:P2}");
            lines.Add($"  Recommendation: {stat.IndexRecommendation}");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build text for schema context.
    /// </summary>
    private string BuildSchemaContextText(SchemaContext schema)
    {
        var lines = new List<string>();

        foreach (var table in schema.Tables)
        {
            lines.Add($"Table: {table.TableName} ({table.RowCount:N0} rows)");

            // Key columns only (PK, FK, Indexed)
            var keyColumns = table.Columns
                .Where(c => c.IsPrimaryKey || c.IsForeignKey || c.HasIndex)
                .ToList();

            if (keyColumns.Any())
            {
                lines.Add("  Key Columns:");
                foreach (var col in keyColumns)
                {
                    var flags = new List<string>();
                    if (col.IsPrimaryKey) flags.Add("PK");
                    if (col.IsForeignKey) flags.Add("FK");
                    if (col.HasIndex) flags.Add("INDEXED");

                    lines.Add($"    {col.ColumnName} {col.DataType} [{string.Join(", ", flags)}]");
                }
            }

            // Indexes
            if (table.Indexes.Any())
            {
                lines.Add("  Indexes:");
                foreach (var idx in table.Indexes.Take(5)) // Top 5 indexes
                {
                    var type = idx.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
                    lines.Add($"    {idx.IndexName} ({type}) ON ({string.Join(", ", idx.Columns)})");
                }
            }

            lines.Add("");
        }

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Represents a context section with priority.
/// </summary>
public class ContextSection
{
    public int Priority { get; set; }       // 1 = highest priority
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
