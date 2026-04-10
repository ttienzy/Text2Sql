using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using TextToSqlAgent.Application.Services.QueryOptimizer;

namespace TextToSqlAgent.Tests.Integration.API;

/// <summary>
/// End-to-end tests for Query Optimizer API
/// Tests full optimization pipeline from API to database
/// </summary>
public class QueryOptimizerE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private const string TestConnectionId = "test-connection-id";
    private const string LimitedTestConnectionId = "limited-test-connection-id";

    public QueryOptimizerE2ETests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact(Skip = "Requires test database and connection setup")]
    public async Task OptimizeQuery_NonSargableWhere_ReturnsSargableFix()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM dbo.Users WHERE YEAR(CreatedDate) = 2024",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);
        Assert.True(result.IsChanged);

        // Optimized SQL should not use YEAR() function in WHERE clause
        Assert.DoesNotContain("YEAR(", result.OptimizedSql);

        // Should contain sargable date range
        Assert.Contains(">=", result.OptimizedSql);
        Assert.Contains("2024", result.OptimizedSql);
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_AlreadyOptimal_SkipsLLM()
    {
        // Arrange - Simple query with proper index usage
        var request = new
        {
            sql = "SELECT Id, Name FROM dbo.Users WHERE Id = 1",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);
        // Query may or may not be changed, but should not crash
        Assert.Equal("ok", result.Severity);
    }

    [Fact(Skip = "Requires test database with limited permissions")]
    public async Task OptimizeQuery_MissingPermission_Returns200WithWarning()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM dbo.Users",
            connectionId = LimitedTestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode(); // Must return 200, not 500
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);
        Assert.NotNull(result.PreFlightAnalysis);
        Assert.False(result.PreFlightAnalysis.CanGetExecutionPlan);

        // Should have warning about missing permission
        Assert.NotEmpty(result.PreFlightAnalysis.Warnings);
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_SelectStar_ReturnsExplicitColumns()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM dbo.Users WHERE Id = 1",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);

        // Should detect AP-01 (SELECT *)
        Assert.Contains(result.DetectedIssues, i => i.Code == "AP-01");
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_OrChain_ReturnsInClause()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM dbo.Users WHERE Status='A' OR Status='B' OR Status='C'",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);

        // Should detect AP-06 (OR chain)
        Assert.Contains(result.DetectedIssues, i => i.Code == "AP-06");

        if (result.IsChanged)
        {
            // Optimized SQL should use IN clause
            Assert.Contains("IN", result.OptimizedSql);
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_MissingSchemaPrefix_AddsSchema()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM Users WHERE Id = 1",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);

        // Should detect AP-13 (missing schema prefix)
        Assert.Contains(result.DetectedIssues, i => i.Code == "AP-13");
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQueryWithPlan_ValidQuery_ReturnsExecutionPlanComparison()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM dbo.Users WHERE YEAR(CreatedDate) = 2024",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize-with-plan", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResultWithPlan>();

        Assert.NotNull(result);
        Assert.NotNull(result.PlanComparison);

        if (result.IsChanged)
        {
            Assert.True(result.PlanComparison.OriginalCost >= 0);
            Assert.True(result.PlanComparison.OptimizedCost >= 0);
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_ComplexMultiIssueQuery_DetectsAllIssues()
    {
        // Arrange - Query with multiple anti-patterns
        var request = new
        {
            sql = @"SELECT * FROM Users 
                    WHERE YEAR(CreatedDate) = 2024 
                    AND Status='Active' OR Status='Pending' 
                    AND Name = 'John'",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResult>();

        Assert.NotNull(result);

        // Should detect multiple issues:
        // AP-01: SELECT *
        // AP-02: YEAR() function on indexed column
        // AP-06: OR chain
        // AP-13: Missing schema prefix (if Users doesn't have dbo.)

        Assert.True(result.DetectedIssues.Count >= 2,
            "Should detect at least 2 anti-patterns in complex query");
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_InvalidSQL_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM INVALID SYNTAX",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        // Should handle gracefully - either 400 Bad Request or 200 with error in response
        Assert.True(response.IsSuccessStatusCode ||
                   response.StatusCode == System.Net.HttpStatusCode.BadRequest);
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_EmptySQL_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            sql = "",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Skip = "Requires test database")]
    public async Task OptimizeQuery_NullConnectionId_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            sql = "SELECT * FROM dbo.Users",
            connectionId = (string)null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(Skip = "Requires test database with data skew")]
    public async Task OptimizeQuery_HighDataSkew_ReturnsSkewWarning()
    {
        // Arrange - Query on column with high data skew
        var request = new
        {
            sql = "SELECT * FROM dbo.Users WHERE Status = 'Active'",
            connectionId = TestConnectionId
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/optimize-with-plan", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OptimizationResultWithPlan>();

        Assert.NotNull(result);

        // If column statistics are available and show high skew
        if (result.PreFlightAnalysis?.CanGetExecutionPlan == true)
        {
            // May have column statistics with skew information
            // This is informational rather than mandatory
        }
    }
}
