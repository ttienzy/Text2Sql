using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Tests.Unit.Services.QueryOptimizer;

/// <summary>
/// Unit tests for ExecutionPlanService.
/// Tests permission checking, warning parsing, and cost driver identification.
/// </summary>
public class ExecutionPlanServiceTests
{
    private readonly ExecutionPlanService _service;
    private readonly Mock<ILogger<ExecutionPlanService>> _loggerMock;

    public ExecutionPlanServiceTests()
    {
        _loggerMock = new Mock<ILogger<ExecutionPlanService>>();
        _service = new ExecutionPlanService(_loggerMock.Object);
    }

    [Fact]
    public async Task CanGetExecutionPlanAsync_WithInvalidConnection_ReturnsFalse()
    {
        // Arrange
        var invalidConnectionString = "Server=invalid;Database=invalid;";

        // Act
        var result = await _service.CanGetExecutionPlanAsync(invalidConnectionString);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetPreFlightAnalysisAsync_WithInvalidConnection_ReturnsGracefulFallback()
    {
        // Arrange
        var invalidConnectionString = "Server=invalid;Database=invalid;";
        var sql = "SELECT * FROM Users";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, invalidConnectionString);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanGetExecutionPlan);
        Assert.True(result.NeedsOptimization);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void ParseWarnings_WithMissingJoinPredicate_ReturnsCriticalWarning()
    {
        // Arrange
        var rawWarnings = new List<string> { "NoJoinPredicate" };

        // Act
        var result = ParseWarningsPublic(rawWarnings);

        // Assert
        Assert.Single(result);
        Assert.Equal(WarningType.MissingJoinPredicate, result[0].Type);
        Assert.Equal(WarningSeverity.Critical, result[0].Severity);
        Assert.Contains("Cartesian product", result[0].Description);
    }

    [Fact]
    public void ParseWarnings_WithMissingStatistics_ReturnsHighSeverityWarning()
    {
        // Arrange
        var rawWarnings = new List<string> { "ColumnsWithNoStatistics", "Missing stats on: UserId, Status" };

        // Act
        var result = ParseWarningsPublic(rawWarnings);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, w => Assert.Equal(WarningType.MissingStatistics, w.Type));
        Assert.All(result, w => Assert.Equal(WarningSeverity.High, w.Severity));
    }

    [Fact]
    public void ParseWarnings_WithSpillToTempDb_ReturnsHighSeverityWarning()
    {
        // Arrange
        var rawWarnings = new List<string> { "SpillToTempDb" };

        // Act
        var result = ParseWarningsPublic(rawWarnings);

        // Assert
        Assert.Single(result);
        Assert.Equal(WarningType.SpillToTempDb, result[0].Type);
        Assert.Equal(WarningSeverity.High, result[0].Severity);
        Assert.Contains("tempdb", result[0].Description);
    }

    [Fact]
    public void ParseWarnings_WithImplicitConversion_ReturnsHighSeverityWarning()
    {
        // Arrange
        var rawWarnings = new List<string> { "ImplicitConversion: CONVERT_IMPLICIT(nvarchar(100),[UserId],0)" };

        // Act
        var result = ParseWarningsPublic(rawWarnings);

        // Assert
        Assert.Single(result);
        Assert.Equal(WarningType.ImplicitConversion, result[0].Type);
        Assert.Equal(WarningSeverity.High, result[0].Severity);
        Assert.Contains("type conversion", result[0].Description);
    }

    [Fact]
    public void ParseWarnings_WithUnknownWarning_ReturnsInfoSeverity()
    {
        // Arrange
        var rawWarnings = new List<string> { "SomeUnknownWarning" };

        // Act
        var result = ParseWarningsPublic(rawWarnings);

        // Assert
        Assert.Single(result);
        Assert.Equal(WarningType.Other, result[0].Type);
        Assert.Equal(WarningSeverity.Info, result[0].Severity);
    }

    [Fact]
    public void IdentifyCostDrivers_WithExpensiveOperators_ReturnsTopFive()
    {
        // Arrange
        var operators = new List<PlanOperator>
        {
            new() { Type = "Clustered Index Scan", EstimatedCost = 10.5, EstimatedRows = 100000, ObjectName = "dbo.Users" },
            new() { Type = "Nested Loops", EstimatedCost = 8.2, EstimatedRows = 50000 },
            new() { Type = "Sort", EstimatedCost = 5.1, EstimatedRows = 20000 },
            new() { Type = "Hash Match", EstimatedCost = 3.5, EstimatedRows = 10000 },
            new() { Type = "Index Seek", EstimatedCost = 0.5, EstimatedRows = 100 },
            new() { Type = "Compute Scalar", EstimatedCost = 0.1, EstimatedRows = 10 }
        };

        // Act
        var result = IdentifyCostDriversPublic(operators);

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal("Clustered Index Scan", result[0].OperatorType);
        Assert.Equal(10.5, result[0].Cost);
        Assert.NotNull(result[0].Recommendation);
    }

    [Fact]
    public void IdentifyCostDrivers_WithTableScan_ReturnsRecommendation()
    {
        // Arrange
        var operators = new List<PlanOperator>
        {
            new() { Type = "Table Scan", EstimatedCost = 15.0, EstimatedRows = 200000, ObjectName = "dbo.Orders" }
        };

        // Act
        var result = IdentifyCostDriversPublic(operators);

        // Assert
        Assert.Single(result);
        Assert.Contains("Full scan", result[0].Recommendation);
        Assert.Contains("covering index", result[0].Recommendation);
    }

    [Fact]
    public void IdentifyCostDrivers_WithExpensiveNestedLoop_ReturnsRecommendation()
    {
        // Arrange
        var operators = new List<PlanOperator>
        {
            new() { Type = "Nested Loops", EstimatedCost = 20.0, EstimatedRows = 50000 }
        };

        // Act
        var result = IdentifyCostDriversPublic(operators);

        // Assert
        Assert.Single(result);
        Assert.Contains("Nested loop on large dataset", result[0].Recommendation);
        Assert.Contains("hash join", result[0].Recommendation);
    }

    [Fact]
    public void IdentifyCostDrivers_WithExpensiveSort_ReturnsRecommendation()
    {
        // Arrange
        var operators = new List<PlanOperator>
        {
            new() { Type = "Sort", EstimatedCost = 5.5, EstimatedRows = 30000 }
        };

        // Act
        var result = IdentifyCostDriversPublic(operators);

        // Assert
        Assert.Single(result);
        Assert.Contains("Expensive sort", result[0].Recommendation);
        Assert.Contains("index to avoid sort", result[0].Recommendation);
    }

    [Fact]
    public void DetermineIfOptimizationNeeded_WithLowCostNoWarnings_ReturnsFalse()
    {
        // Arrange
        var plan = new ExecutionPlan
        {
            EstimatedTotalCost = 0.05,
            Warnings = new List<string>(),
            MissingIndexes = new List<MissingIndex>(),
            Operators = new List<PlanOperator>()
        };

        // Act
        var result = DetermineIfOptimizationNeededPublic(plan);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DetermineIfOptimizationNeeded_WithWarnings_ReturnsTrue()
    {
        // Arrange
        var plan = new ExecutionPlan
        {
            EstimatedTotalCost = 0.05,
            Warnings = new List<string> { "NoJoinPredicate" },
            MissingIndexes = new List<MissingIndex>(),
            Operators = new List<PlanOperator>()
        };

        // Act
        var result = DetermineIfOptimizationNeededPublic(plan);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DetermineIfOptimizationNeeded_WithHighImpactMissingIndex_ReturnsTrue()
    {
        // Arrange
        var plan = new ExecutionPlan
        {
            EstimatedTotalCost = 0.05,
            Warnings = new List<string>(),
            MissingIndexes = new List<MissingIndex>
            {
                new() { Impact = 15.5, Table = "Users" }
            },
            Operators = new List<PlanOperator>()
        };

        // Act
        var result = DetermineIfOptimizationNeededPublic(plan);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DetermineIfOptimizationNeeded_WithExpensiveScan_ReturnsTrue()
    {
        // Arrange
        var plan = new ExecutionPlan
        {
            EstimatedTotalCost = 5.0,
            Warnings = new List<string>(),
            MissingIndexes = new List<MissingIndex>(),
            Operators = new List<PlanOperator>
            {
                new() { Type = "Clustered Index Scan", EstimatedCost = 3.5 }
            }
        };

        // Act
        var result = DetermineIfOptimizationNeededPublic(plan);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void MissingIndex_GenerateCreateStatement_WithEqualityColumns_ReturnsValidSQL()
    {
        // Arrange
        var missingIndex = new MissingIndex
        {
            Schema = "dbo",
            Table = "Users",
            Impact = 25.5,
            EqualityColumns = new List<string> { "UserId", "Status" },
            InequalityColumns = new List<string>(),
            IncludedColumns = new List<string> { "FirstName", "LastName" }
        };

        // Act
        var result = missingIndex.GenerateCreateStatement();

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("CREATE NONCLUSTERED INDEX", result);
        Assert.Contains("[dbo].[Users]", result);
        Assert.Contains("[UserId]", result);
        Assert.Contains("[Status]", result);
        Assert.Contains("INCLUDE", result);
        Assert.Contains("[FirstName]", result);
        Assert.Contains("25.5%", result);
    }

    [Fact]
    public void MissingIndex_GenerateCreateStatement_WithoutIncludeColumns_ReturnsValidSQL()
    {
        // Arrange
        var missingIndex = new MissingIndex
        {
            Schema = "dbo",
            Table = "Orders",
            Impact = 10.0,
            EqualityColumns = new List<string> { "CustomerId" },
            InequalityColumns = new List<string> { "OrderDate" },
            IncludedColumns = new List<string>()
        };

        // Act
        var result = missingIndex.GenerateCreateStatement();

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("CREATE NONCLUSTERED INDEX", result);
        Assert.Contains("[CustomerId]", result);
        Assert.Contains("[OrderDate]", result);
        Assert.DoesNotContain("INCLUDE", result);
    }

    [Fact]
    public void MissingIndex_GenerateCreateStatement_WithEmptyColumns_ReturnsEmpty()
    {
        // Arrange
        var missingIndex = new MissingIndex
        {
            Schema = "dbo",
            Table = "Users",
            Impact = 5.0,
            EqualityColumns = new List<string>(),
            InequalityColumns = new List<string>(),
            IncludedColumns = new List<string>()
        };

        // Act
        var result = missingIndex.GenerateCreateStatement();

        // Assert
        Assert.Empty(result);
    }

    // Helper methods to access private methods via reflection
    // In production, these would be internal or protected for testing

    private List<PlanWarning> ParseWarningsPublic(List<string> rawWarnings)
    {
        var method = typeof(ExecutionPlanService).GetMethod("ParseWarnings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (List<PlanWarning>)method!.Invoke(_service, new object[] { rawWarnings })!;
    }

    private List<CostDriver> IdentifyCostDriversPublic(List<PlanOperator> operators)
    {
        var method = typeof(ExecutionPlanService).GetMethod("IdentifyCostDrivers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (List<CostDriver>)method!.Invoke(_service, new object[] { operators })!;
    }

    private bool DetermineIfOptimizationNeededPublic(ExecutionPlan plan)
    {
        var method = typeof(ExecutionPlanService).GetMethod("DetermineIfOptimizationNeeded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (bool)method!.Invoke(_service, new object[] { plan })!;
    }
}
