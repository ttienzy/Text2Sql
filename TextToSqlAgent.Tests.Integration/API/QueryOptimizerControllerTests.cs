using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using TextToSqlAgent.API.DTOs.QueryOptimizer;

namespace TextToSqlAgent.Tests.Integration.API;

/// <summary>
/// Integration tests for QueryOptimizerController
/// Tests the /api/query-optimizer/analyze endpoint
/// </summary>
[Collection("Integration Tests")]
public class QueryOptimizerControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public QueryOptimizerControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AnalyzeQuery_WithValidSimpleQuery_ReturnsSuccess()
    {
        // Arrange
        var request = new OptimizeQueryRequest
        {
            Sql = "SELECT * FROM Users WHERE Id = 1",
            ConnectionId = 1,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OptimizeQueryResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.OriginalSql);
        Assert.NotNull(result.OptimizedSql);
        Assert.NotNull(result.DetectedIssues);
    }

    [Fact]
    public async Task AnalyzeQuery_WithSelectStar_DetectsAntiPattern()
    {
        // Arrange
        var request = new OptimizeQueryRequest
        {
            Sql = "SELECT * FROM Users",
            ConnectionId = 1,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OptimizeQueryResponse>();
        Assert.NotNull(result);
        Assert.Contains(result.DetectedIssues, i => i.Code == "AP-01");
    }

    [Fact]
    public async Task AnalyzeQuery_WithEmptySQL_ReturnsBadRequest()
    {
        // Arrange
        var request = new OptimizeQueryRequest
        {
            Sql = "",
            ConnectionId = 1,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnalyzeQuery_WithInvalidConnectionId_ReturnsNotFound()
    {
        // Arrange
        var request = new OptimizeQueryRequest
        {
            Sql = "SELECT * FROM Users",
            ConnectionId = 99999,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnalyzeQuery_WithComplexQuery_ReturnsComplexityScore()
    {
        // Arrange
        var request = new OptimizeQueryRequest
        {
            Sql = @"
                WITH ActiveUsers AS (
                    SELECT * FROM Users WHERE Status = 'Active'
                )
                SELECT u.Name, o.OrderDate
                FROM ActiveUsers u
                INNER JOIN Orders o ON u.Id = o.UserId
                WHERE YEAR(o.OrderDate) = 2024",
            ConnectionId = 1,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OptimizeQueryResponse>();
        Assert.NotNull(result);
        Assert.True(result.ComplexityScore > 0);
        Assert.NotNull(result.ModelUsed);
    }

    [Fact]
    public async Task AnalyzeQuery_WithMultipleAntiPatterns_DetectsAll()
    {
        // Arrange
        var request = new OptimizeQueryRequest
        {
            Sql = @"
                SELECT * 
                FROM Users 
                WHERE YEAR(CreatedDate) = 2024 
                AND Name LIKE '%test%'",
            ConnectionId = 1,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OptimizeQueryResponse>();
        Assert.NotNull(result);
        Assert.True(result.DetectedIssues.Count >= 3,
            $"Expected at least 3 issues, got {result.DetectedIssues.Count}");
    }

    [Fact]
    public async Task AnalyzeQuery_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        var unauthenticatedClient = _factory.CreateClient();
        // Remove authentication header if any

        var request = new OptimizeQueryRequest
        {
            Sql = "SELECT * FROM Users",
            ConnectionId = 1,
            IncludeExecutionPlan = false
        };

        // Act
        var response = await unauthenticatedClient.PostAsJsonAsync("/api/query-optimizer/analyze", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
