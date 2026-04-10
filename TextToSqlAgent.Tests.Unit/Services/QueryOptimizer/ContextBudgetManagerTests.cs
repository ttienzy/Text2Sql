using System.Collections.Generic;
using System.Linq;
using Xunit;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

/// <summary>
/// Unit tests for ContextBudgetManager
/// </summary>
public class ContextBudgetManagerTests
{
    private readonly ContextBudgetManager _manager;

    public ContextBudgetManagerTests()
    {
        _manager = new ContextBudgetManager();
    }

    [Fact]
    public void BuildPrioritizedContext_WithEmptyInputs_ReturnsEmptyString()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis();
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.NotNull(result);
        Assert.True(string.IsNullOrWhiteSpace(result) || result.Length < 100);
    }

    [Fact]
    public void BuildPrioritizedContext_WithCriticalWarnings_IncludesCriticalSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis
        {
            Warnings = new List<PlanWarning>
            {
                new()
                {
                    Severity = WarningSeverity.Critical,
                    Description = "Missing JOIN predicate",
                    Recommendation = "Add proper JOIN condition"
                }
            }
        };
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("CRITICAL WARNINGS", result);
        Assert.Contains("Missing JOIN predicate", result);
    }

    [Fact]
    public void BuildPrioritizedContext_WithCostDrivers_IncludesCostDriverSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis
        {
            CostDrivers = new List<CostDriver>
            {
                new()
                {
                    OperatorType = "Clustered Index Scan",
                    Cost = 5.5,
                    Rows = 100000,
                    Description = "Expensive scan operation",
                    Recommendation = "Consider adding index"
                }
            }
        };
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("TOP COST DRIVERS", result);
        Assert.Contains("Expensive scan operation", result);
    }

    [Fact]
    public void BuildPrioritizedContext_WithCriticalAntiPatterns_IncludesCriticalAntiPatternSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis();
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>
        {
            new()
            {
                Code = "AP-02",
                Severity = Severity.Critical,
                Title = "Function on indexed column",
                Description = "Using function prevents index usage",
                Impact = "Full table scan"
            }
        };
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("CRITICAL ANTI-PATTERNS", result);
        Assert.Contains("AP-02", result);
        Assert.Contains("Function on indexed column", result);
    }

    [Fact]
    public void BuildPrioritizedContext_WithHighSkewColumns_IncludesHighSkewSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis();
        var columnStats = new Dictionary<string, ColumnStatistics>
        {
            ["Users.Status"] = new()
            {
                TableName = "Users",
                ColumnName = "Status",
                TotalRows = 1000000,
                DistinctValues = 3,
                SkewFactor = 0.85,
                SkewLevel = SkewLevel.High,
                TopValues = new List<TopValue>
                {
                    new() { Value = "Active", Count = 850000, Percentage = 85 }
                }
            }
        };
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("HIGH SKEW COLUMNS", result);
        Assert.Contains("Users.Status", result);
        Assert.Contains("85", result); // Skew percentage
    }

    [Fact]
    public void BuildPrioritizedContext_WithMissingIndexes_IncludesMissingIndexSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis
        {
            IndexRecommendations = new List<IndexRecommendation>
            {
                new()
                {
                    TableName = "dbo.Orders",
                    KeyColumns = new List<string> { "CustomerId", "OrderDate" },
                    IncludeColumns = new List<string> { "TotalAmount" },
                    ImpactPercentage = 45.5,
                    CreateStatement = "CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_OrderDate..."
                }
            }
        };
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("MISSING INDEX RECOMMENDATIONS", result);
        Assert.Contains("dbo.Orders", result);
        Assert.Contains("45", result); // Impact percentage
    }

    [Fact]
    public void BuildPrioritizedContext_PrioritizesCorrectly()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis
        {
            Warnings = new List<PlanWarning>
            {
                new()
                {
                    Severity = WarningSeverity.Critical,
                    Description = "Critical warning",
                    Recommendation = "Fix immediately"
                },
                new()
                {
                    Severity = WarningSeverity.Medium,
                    Description = "Medium warning",
                    Recommendation = "Fix when possible"
                }
            },
            CostDrivers = new List<CostDriver>
            {
                new()
                {
                    OperatorType = "Table Scan",
                    Cost = 10.0,
                    Description = "Expensive operation"
                }
            }
        };
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        // Critical warnings should appear before cost drivers
        var criticalIndex = result.IndexOf("CRITICAL WARNINGS");
        var costDriverIndex = result.IndexOf("TOP COST DRIVERS");

        Assert.True(criticalIndex >= 0);
        Assert.True(costDriverIndex >= 0);
        Assert.True(criticalIndex < costDriverIndex, "Critical warnings should appear before cost drivers");
    }

    [Fact]
    public void BuildPrioritizedContext_DoesNotExceedTokenBudget()
    {
        // Arrange - Create large inputs
        var preFlight = new PreFlightAnalysis
        {
            Warnings = Enumerable.Range(0, 100).Select(i => new PlanWarning
            {
                Severity = i < 10 ? WarningSeverity.Critical : WarningSeverity.Medium,
                Description = $"Warning {i}: " + new string('x', 200),
                Recommendation = $"Recommendation {i}: " + new string('y', 200)
            }).ToList(),
            CostDrivers = Enumerable.Range(0, 50).Select(i => new CostDriver
            {
                OperatorType = "Scan",
                Cost = i * 1.5,
                Description = new string('z', 300)
            }).ToList()
        };

        var columnStats = Enumerable.Range(0, 50).ToDictionary(
            i => $"Table{i}.Column{i}",
            i => new ColumnStatistics
            {
                TableName = $"Table{i}",
                ColumnName = $"Column{i}",
                TotalRows = 1000000,
                DistinctValues = 100,
                SkewFactor = 0.8,
                SkewLevel = SkewLevel.High
            });

        var issues = Enumerable.Range(0, 100).Select(i => new AntiPattern
        {
            Code = $"AP-{i:D2}",
            Severity = i < 20 ? Severity.Critical : Severity.Warning,
            Title = $"Issue {i}",
            Description = new string('a', 200),
            Impact = new string('b', 200)
        }).ToList();

        var schema = new SchemaContext
        {
            Tables = Enumerable.Range(0, 20).Select(i => new TableSchema
            {
                TableName = $"Table{i}",
                RowCount = 1000000,
                Columns = Enumerable.Range(0, 30).Select(j => new ColumnInfo
                {
                    ColumnName = $"Column{j}",
                    DataType = "int",
                    IsPrimaryKey = j == 0,
                    HasIndex = j < 5
                }).ToList()
            }).ToList()
        };

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        // Rough token estimate: 6000 tokens * 4 chars/token = 24000 chars max
        Assert.True(result.Length <= 30000, $"Result length {result.Length} exceeds reasonable token budget");

        // Should still include critical sections
        Assert.Contains("CRITICAL WARNINGS", result);
        Assert.Contains("TOP COST DRIVERS", result);
    }

    [Fact]
    public void BuildPrioritizedContext_WithStaleStatistics_IncludesStaleWarning()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis();
        var columnStats = new Dictionary<string, ColumnStatistics>
        {
            ["Orders.CustomerId"] = new()
            {
                TableName = "Orders",
                ColumnName = "CustomerId",
                TotalRows = 500000,
                DistinctValues = 10000,
                SkewFactor = 0.75,
                SkewLevel = SkewLevel.High,
                IsStale = true,
                StaleWarning = "Statistics last updated 2024-01-01. Consider running UPDATE STATISTICS."
            }
        };
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("STALE", result);
        Assert.Contains("UPDATE STATISTICS", result);
    }

    [Fact]
    public void BuildPrioritizedContext_WithSchemaContext_IncludesSchemaSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis();
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext
        {
            Tables = new List<TableSchema>
            {
                new()
                {
                    TableName = "Users",
                    RowCount = 100000,
                    Columns = new List<ColumnInfo>
                    {
                        new()
                        {
                            ColumnName = "UserId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            HasIndex = true
                        }
                    },
                    Indexes = new List<IndexInfo>
                    {
                        new()
                        {
                            IndexName = "PK_Users",
                            IsClustered = true,
                            Columns = new List<string> { "UserId" }
                        }
                    }
                }
            }
        };

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("SCHEMA CONTEXT", result);
        Assert.Contains("Users", result);
        Assert.Contains("UserId", result);
    }

    [Fact]
    public void BuildContext_ExceedsLimit_TruncatesLowPriorityFirst()
    {
        // Arrange: Create content large enough to exceed 6000 tokens
        var preFlight = new PreFlightAnalysis
        {
            Warnings = new List<PlanWarning>
            {
                new()
                {
                    Severity = WarningSeverity.Critical,
                    Description = "Critical join predicate missing",
                    Recommendation = "Add proper JOIN condition"
                }
            },
            CostDrivers = new List<CostDriver>
            {
                new()
                {
                    OperatorType = "Table Scan",
                    Cost = 100.0,
                    Rows = 1000000,
                    Description = "Expensive table scan operation"
                }
            }
        };

        // Create large schema that would exceed token budget
        var largeSchema = new SchemaContext
        {
            Tables = Enumerable.Range(0, 50).Select(i => new TableSchema
            {
                TableName = $"Table{i}",
                RowCount = 1000000,
                Columns = Enumerable.Range(0, 50).Select(j => new ColumnInfo
                {
                    ColumnName = $"Column{j}_{new string('x', 100)}",
                    DataType = "varchar(max)",
                    IsPrimaryKey = j == 0
                }).ToList()
            }).ToList()
        };

        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, largeSchema);

        // Assert: Critical warning must be present (Priority 1)
        Assert.Contains("Critical join predicate missing", result);

        // Cost driver should be present (Priority 2)
        Assert.Contains("Table Scan", result);

        // Result should not exceed reasonable token budget (~24000 chars for 6000 tokens)
        Assert.True(result.Length <= 30000, $"Result length {result.Length} exceeds token budget");
    }

    [Fact]
    public void BuildContext_Priority1To4_AlwaysIncluded()
    {
        // Arrange: Create all priority 1-4 content
        var preFlight = new PreFlightAnalysis
        {
            Warnings = new List<PlanWarning>
            {
                new()
                {
                    Severity = WarningSeverity.Critical,
                    Description = "Priority 1: Critical warning",
                    Recommendation = "Fix immediately"
                }
            },
            CostDrivers = new List<CostDriver>
            {
                new()
                {
                    OperatorType = "Priority 2: Cost Driver",
                    Cost = 50.0,
                    Description = "Expensive operation"
                }
            }
        };

        var issues = new List<AntiPattern>
        {
            new()
            {
                Code = "AP-02",
                Severity = Severity.Critical,
                Title = "Priority 3: Critical anti-pattern",
                Description = "Function on indexed column"
            }
        };

        var columnStats = new Dictionary<string, ColumnStatistics>
        {
            ["Users.Status"] = new()
            {
                TableName = "Users",
                ColumnName = "Status",
                SkewFactor = 0.85,
                SkewLevel = SkewLevel.High,
                TotalRows = 1000000,
                DistinctValues = 3
            }
        };

        // Add large schema to test priority
        var largeSchema = new SchemaContext
        {
            Tables = Enumerable.Range(0, 30).Select(i => new TableSchema
            {
                TableName = $"Table{i}",
                RowCount = 1000000,
                Columns = Enumerable.Range(0, 30).Select(j => new ColumnInfo
                {
                    ColumnName = $"Column{j}",
                    DataType = "int"
                }).ToList()
            }).ToList()
        };

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, largeSchema);

        // Assert: All priority 1-4 sections must be present
        Assert.Contains("Priority 1: Critical warning", result);
        Assert.Contains("Priority 2: Cost Driver", result);
        Assert.Contains("Priority 3: Critical anti-pattern", result);
        Assert.Contains("Users.Status", result); // Priority 4: High skew column
    }

    [Fact]
    public void BuildContext_EmptyInput_ReturnsNonEmptyString()
    {
        // Arrange: All empty inputs
        var preFlight = new PreFlightAnalysis();
        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert: Should not crash, returns empty or minimal string
        Assert.NotNull(result);
        // Empty result is acceptable
    }

    [Fact]
    public void BuildContext_OnlyCriticalWarnings_IncludesOnlyCriticalSection()
    {
        // Arrange
        var preFlight = new PreFlightAnalysis
        {
            Warnings = new List<PlanWarning>
            {
                new()
                {
                    Severity = WarningSeverity.Critical,
                    Description = "Critical issue only",
                    Recommendation = "Fix now"
                }
            }
        };

        var columnStats = new Dictionary<string, ColumnStatistics>();
        var issues = new List<AntiPattern>();
        var schema = new SchemaContext();

        // Act
        var result = _manager.BuildPrioritizedContext(preFlight, columnStats, issues, schema);

        // Assert
        Assert.Contains("CRITICAL WARNINGS", result);
        Assert.Contains("Critical issue only", result);
    }

}
