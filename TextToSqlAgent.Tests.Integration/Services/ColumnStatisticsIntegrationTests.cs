using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using TextToSqlAgent.Application.Services.QueryOptimizer;

namespace TextToSqlAgent.Tests.Integration.Services;

/// <summary>
/// Integration tests for ColumnStatisticsService
/// Requires actual database connection for full testing
/// </summary>
public class ColumnStatisticsIntegrationTests
{
    private readonly ColumnStatisticsService _service;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<ColumnStatisticsService>> _loggerMock;

    // Note: Replace with actual test database connection string
    private const string TestConnectionString = "Server=localhost;Database=TestDb;Integrated Security=true;TrustServerCertificate=true;";

    public ColumnStatisticsIntegrationTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<ColumnStatisticsService>>();
        _service = new ColumnStatisticsService(_cacheMock.Object, _loggerMock.Object);
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetColumnStatisticsAsync_ShouldReturnValidSkewFactor()
    {
        // Arrange
        var tableName = "Users";
        var columnName = "Status";

        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.SkewFactor >= 0 && stats.SkewFactor <= 1,
            "SkewFactor should be between 0 and 1");
        Assert.True(stats.Selectivity >= 0 && stats.Selectivity <= 1,
            "Selectivity should be between 0 and 1");
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetColumnStatisticsAsync_ShouldReturnTopValues()
    {
        // Arrange
        var tableName = "Users";
        var columnName = "Status";

        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Assert
        Assert.NotNull(stats);
        Assert.NotEmpty(stats.TopValues);
        Assert.All(stats.TopValues, tv =>
        {
            Assert.True(tv.Count > 0);
            Assert.True(tv.Percentage >= 0 && tv.Percentage <= 100);
        });
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetColumnStatisticsAsync_ShouldCacheResults()
    {
        // Arrange
        var tableName = "Users";
        var columnName = "Status";

        // Act - First call
        var stats1 = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Act - Second call (should hit cache)
        var stats2 = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Assert
        Assert.NotNull(stats1);
        Assert.NotNull(stats2);

        // Verify cache was called
        _cacheMock.Verify(c => c.GetStringAsync(
            It.IsAny<string>(),
            It.IsAny<System.Threading.CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task GetColumnStatisticsAsync_WithInvalidConnection_ShouldReturnNull()
    {
        // Arrange
        var invalidConnectionString = "Server=invalid;Database=invalid;";

        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            "Users", "Status", invalidConnectionString);

        // Assert
        Assert.Null(stats);
    }

    [Fact]
    public async Task InvalidateTableStatisticsCacheAsync_ShouldNotThrow()
    {
        // Arrange
        var tableName = "Users";

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () =>
            await _service.InvalidateTableStatisticsCacheAsync(tableName));

        Assert.Null(exception);
    }

    [Fact(Skip = "Requires test database with stale statistics")]
    public async Task GetColumnStatisticsAsync_WithStaleStats_ShouldSetIsStale()
    {
        // Arrange
        var tableName = "Users";
        var columnName = "Status";

        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Assert
        Assert.NotNull(stats);

        // If stats are stale (> 7 days or 20% modifications)
        if (stats.IsStale)
        {
            Assert.NotNull(stats.StaleWarning);
            Assert.Contains("UPDATE STATISTICS", stats.StaleWarning);
        }
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetTableStatisticsAsync_ShouldReturnMultipleColumns()
    {
        // Arrange
        var tableName = "Users";
        var columns = new System.Collections.Generic.List<string> { "Status", "Country", "Age" };

        // Act
        var stats = await _service.GetTableStatisticsAsync(
            tableName, columns, TestConnectionString);

        // Assert
        Assert.NotEmpty(stats);
        Assert.All(stats.Values, s =>
        {
            Assert.NotNull(s.IndexRecommendation);
            Assert.True(s.TotalRows > 0);
        });
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetColumnStatisticsAsync_ShouldIncludeModificationCounter()
    {
        // Arrange
        var tableName = "Users";
        var columnName = "Status";

        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.ModificationCounter >= 0);
    }

    [Fact(Skip = "Requires test database")]
    public async Task GetColumnStatisticsAsync_ShouldIncludeLastUpdated()
    {
        // Arrange
        var tableName = "Users";
        var columnName = "Status";

        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            tableName, columnName, TestConnectionString);

        // Assert
        Assert.NotNull(stats);

        // LastUpdated may be null if no statistics exist
        if (stats.LastUpdated.HasValue)
        {
            Assert.True(stats.LastUpdated.Value <= DateTime.UtcNow);
        }
    }
}
