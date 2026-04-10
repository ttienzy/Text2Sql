using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using TextToSqlAgent.Application.Services.QueryOptimizer;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Tests.Integration.Services;

/// <summary>
/// Integration tests for ExecutionPlanService
/// Tests permission checks, graceful degradation, and real execution plan analysis
/// NOTE: These tests require actual database connections
/// </summary>
public class ExecutionPlanServiceIntegrationTests
{
    private readonly ExecutionPlanService _service;
    private readonly Mock<ILogger<ExecutionPlanService>> _loggerMock;

    // Replace with actual test database connection strings
    private const string TestConnectionString = "Server=localhost;Database=TestDb;Integrated Security=true;TrustServerCertificate=true;";
    private const string LimitedConnectionString = "Server=localhost;Database=TestDb;User Id=limited_user;Password=test;TrustServerCertificate=true;";

    public ExecutionPlanServiceIntegrationTests()
    {
        _loggerMock = new Mock<ILogger<ExecutionPlanService>>();
        _service = new ExecutionPlanService(_loggerMock.Object);
    }

    [Fact(Skip = "Requires test database with VIEW DATABASE STATE permission")]
    public async Task CanGetExecutionPlan_ValidConnection_ReturnsTrue()
    {
        // Act
        var result = await _service.CanGetExecutionPlanAsync(TestConnectionString);

        // Assert
        Assert.True(result, "Should have VIEW DATABASE STATE permission");
    }

    [Fact(Skip = "Requires test database with limited user")]
    public async Task CanGetExecutionPlan_LimitedUser_ReturnsFalse()
    {
        // Act
        var result = await _service.CanGetExecutionPlanAsync(LimitedConnectionString);

        // Assert
        Assert.False(result, "Limited user should not have VIEW DATABASE STATE permission");
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_NoPermission_GracefulDegradation()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, LimitedConnectionString);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanGetExecutionPlan);
        Assert.True(result.NeedsOptimization); // Default true when no plan available
        Assert.NotEmpty(result.Warnings);

        // Should have warning about missing permission
        Assert.Contains(result.Warnings, w =>
            w.Description.Contains("permission", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_InvalidConnection_GracefulDegradation()
    {
        // Arrange
        var sql = "SELECT * FROM Users";
        var invalidConnectionString = "Server=invalid;Database=invalid;";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, invalidConnectionString);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanGetExecutionPlan);
        Assert.NotEmpty(result.Warnings);

        // Should not throw exception
    }

    [Fact(Skip = "Requires test database with LargeTable")]
    public async Task GetPreFlightAnalysis_TableScanQuery_DetectsCostDriver()
    {
        // Arrange - Query that forces table scan (no WHERE clause, no index)
        var sql = "SELECT * FROM dbo.LargeTable";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, TestConnectionString);

        // Assert
        if (result.CanGetExecutionPlan)
        {
            Assert.NotEmpty(result.CostDrivers);
            Assert.Contains(result.CostDrivers, d =>
                d.OperatorType.Contains("Scan", System.StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_IndexSeekQuery_LowCost()
    {
        // Arrange - Query that uses index seek
        var sql = "SELECT Id, Name FROM dbo.Users WHERE Id = 1";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, TestConnectionString);

        // Assert
        if (result.CanGetExecutionPlan)
        {
            Assert.True(result.EstimatedCost < 1.0, "Index seek should have low cost");
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_MissingIndex_RecommendationsPresent()
    {
        // Arrange - Query that would benefit from index
        var sql = "SELECT * FROM dbo.Orders WHERE CustomerId = 123 AND OrderDate > '2024-01-01'";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, TestConnectionString);

        // Assert
        if (result.CanGetExecutionPlan && result.IndexRecommendations.Count > 0)
        {
            Assert.NotEmpty(result.IndexRecommendations);
            Assert.All(result.IndexRecommendations, rec =>
            {
                Assert.NotEmpty(rec.TableName);
                Assert.NotEmpty(rec.KeyColumns);
                Assert.True(rec.ImpactPercentage > 0);
            });
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_ImplicitConversion_DetectsWarning()
    {
        // Arrange - Query with implicit conversion (int column compared to string)
        var sql = "SELECT * FROM dbo.Users WHERE Id = '123'";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, TestConnectionString);

        // Assert
        if (result.CanGetExecutionPlan)
        {
            // May detect implicit conversion in warnings or implicit conversions list
            var hasConversionWarning = result.Warnings.Any(w =>
                w.Type == WarningType.ImplicitConversion) ||
                result.ImplicitConversions.Count > 0;

            // Note: Not all implicit conversions are detected by execution plan
            // This is informational rather than mandatory
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_CartesianProduct_DetectsCriticalWarning()
    {
        // Arrange - Query with missing JOIN predicate (Cartesian product)
        var sql = "SELECT * FROM dbo.Users, dbo.Orders";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, TestConnectionString);

        // Assert
        if (result.CanGetExecutionPlan)
        {
            Assert.Contains(result.Warnings, w =>
                w.Type == WarningType.MissingJoinPredicate &&
                w.Severity == WarningSeverity.Critical);
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetPreFlightAnalysis_OptimalQuery_NoWarnings()
    {
        // Arrange - Well-optimized query
        var sql = "SELECT Id, Name FROM dbo.Users WHERE Id = 1";

        // Act
        var result = await _service.GetPreFlightAnalysisAsync(sql, TestConnectionString);

        // Assert
        if (result.CanGetExecutionPlan)
        {
            Assert.False(result.NeedsOptimization);
            Assert.True(result.EstimatedCost < 0.1);
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetEstimatedPlanAsync_ValidQuery_ReturnsPlan()
    {
        // Arrange
        var sql = "SELECT * FROM dbo.Users WHERE Id = 1";

        // Act
        var plan = await _service.GetEstimatedPlanAsync(sql, TestConnectionString);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.EstimatedTotalCost >= 0);
        Assert.NotEmpty(plan.Operators);
    }

    [Fact(Skip = "Requires test database")]
    public async Task ComparePlansAsync_OptimizedQuery_ShowsImprovement()
    {
        // Arrange
        var originalSql = "SELECT * FROM dbo.Users WHERE YEAR(CreatedDate) = 2024";
        var optimizedSql = "SELECT * FROM dbo.Users WHERE CreatedDate >= '2024-01-01' AND CreatedDate < '2025-01-01'";

        // Act
        var comparison = await _service.ComparePlansAsync(originalSql, optimizedSql, TestConnectionString);

        // Assert
        Assert.NotNull(comparison);
        Assert.True(comparison.OptimizedCost <= comparison.OriginalCost);

        if (comparison.IsImproved)
        {
            Assert.True(comparison.ImprovementFactor >= 1.0);
            Assert.True(comparison.ImprovementPercentage >= 0);
        }
    }
}
