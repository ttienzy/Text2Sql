using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tasks;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.Tasks;

public class IntentRoutingTaskTests
{
    private readonly IntentRoutingTask _task;

    public IntentRoutingTaskTests()
    {
        _task = new IntentRoutingTask(NullLogger<IntentRoutingTask>.Instance);
    }

    #region Intent Classification Tests

    [Theory]
    [InlineData("Có bao nhiêu khách hàng?", IntentCategory.Query)]
    [InlineData("Liệt kê các đơn hàng trong tháng này", IntentCategory.Query)]
    [InlineData("Tổng doanh thu theo tháng", IntentCategory.Query)]
    [InlineData("Top 10 sản phẩm bán chạy nhất", IntentCategory.Query)]
    public async Task ShouldClassifyQueryIntent(string input, IntentCategory expectedIntent)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Intent.Should().Be(expectedIntent);
    }

    [Theory]
    [InlineData("Thêm khách hàng mới", IntentCategory.Insert)]
    [InlineData("Chèn một bản ghi vào bảng Orders", IntentCategory.Insert)]
    [InlineData("Tạo mới sản phẩm", IntentCategory.Insert)]
    public async Task ShouldClassifyInsertIntent(string input, IntentCategory expectedIntent)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Intent.Should().Be(expectedIntent);
    }

    [Theory]
    [InlineData("Cập nhật giá sản phẩm", IntentCategory.Update)]
    [InlineData("Sửa địa chỉ khách hàng thành HCM", IntentCategory.Update)]
    public async Task ShouldClassifyUpdateIntent(string input, IntentCategory expectedIntent)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Intent.Should().Be(expectedIntent);
    }

    [Theory]
    [InlineData("Xóa đơn hàng này", IntentCategory.Delete)]
    [InlineData("Loại bỏ bản ghi cũ", IntentCategory.Delete)]
    public async Task ShouldClassifyDeleteIntent(string input, IntentCategory expectedIntent)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Intent.Should().Be(expectedIntent);
    }

    [Theory]
    [InlineData("drop table Customers", IntentCategory.Forbidden)]
    [InlineData("truncate table Orders", IntentCategory.Forbidden)]
    public async Task ShouldClassifyForbiddenIntent(string input, IntentCategory expectedIntent)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Intent.Should().Be(expectedIntent);
    }

    [Fact]
    public async Task ShouldUsePromptServiceForClarificationWhenAvailable()
    {
        var task = new IntentRoutingTask(
            NullLogger<IntentRoutingTask>.Instance,
            new StubIntentRoutingPromptService
            {
                ClarificationResponse = "Please specify the time period you want to inspect."
            });

        var result = await task.ExecuteAsync("top");

        result.Ambiguity.IsAmbiguous.Should().BeTrue();
        result.Ambiguity.Suggestions.Should().Contain("Please specify the time period you want to inspect.");
    }

    [Fact]
    public async Task ShouldUsePromptServiceForRejectionMessageWhenAvailable()
    {
        var task = new IntentRoutingTask(
            NullLogger<IntentRoutingTask>.Instance,
            new StubIntentRoutingPromptService
            {
                RejectionResponse = "Blocked for safety. Use a read-only preview query instead."
            });

        var result = await task.ExecuteAsync("drop table Customers");

        result.RejectionMessage.Should().Be("Blocked for safety. Use a read-only preview query instead.");
    }

    #endregion

    #region Pipeline Routing Tests

    [Theory]
    [InlineData("Liệt kê khách hàng", PipelineRoute.Query)]
    [InlineData("Có bao nhiêu đơn hàng?", PipelineRoute.Query)]
    public async Task ShouldRouteQueryToQueryPipeline(string input, PipelineRoute expectedRoute)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Route.Should().Be(expectedRoute);
    }

    [Theory]
    [InlineData("Xóa đơn hàng 123")]
    [InlineData("Cập nhật giá sản phẩm thành 100")]
    public async Task ShouldRouteDmlToDmlPipeline(string input)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Route.Should().Be(PipelineRoute.Dml);
    }

    [Theory]
    [InlineData("drop table Customers")]
    public async Task ShouldRouteForbiddenToForbiddenPipeline(string input)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Route.Should().Be(PipelineRoute.Forbidden);
    }

    #endregion

    #region Risk Assessment Tests

    [Theory]
    [InlineData("Liệt kê khách hàng", RiskLevel.Low)]
    [InlineData("Tổng doanh thu", RiskLevel.Low)]
    public async Task ShouldAssessLowRiskForReadOnlyQueries(string input, RiskLevel expectedRisk)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.RiskLevel.Should().Be(expectedRisk);
    }

    [Fact]
    public async Task ShouldAssessHighRiskForDeleteWithWhere()
    {
        // Arrange
        var input = "Xóa đơn hàng có status = 'cancelled'";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.RiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public async Task ShouldAssessMediumRiskForInsert()
    {
        // Arrange
        var input = "Thêm khách hàng mới";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.RiskLevel.Should().BeOneOf(RiskLevel.Medium, RiskLevel.High);
    }

    #endregion

    #region Complexity Scoring Tests

    [Theory]
    [InlineData("Liệt kê khách hàng", 0.0, 0.1)] // Simple query
    [InlineData("Top 10 khách hàng", 0.0, 0.1)]   // Still simple
    public async Task ShouldScoreLowComplexityForSimpleQueries(string input, double min, double max)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.ComplexityScore.Should().BeGreaterThanOrEqualTo(min);
        result.ComplexityScore.Should().BeLessThan(max);
    }

    [Theory]
    [InlineData("Tổng doanh thu theo tháng có join với bảng khách hàng")]
    [InlineData("Top 10 sản phẩm có nhiều join nhất")]
    public async Task ShouldScoreHigherComplexityForQueriesWithJoins(string input)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.ComplexityScore.Should().BeGreaterThan(0.2);
    }

    #endregion

    #region Ambiguity Detection Tests

    [Fact]
    public async Task ShouldDetectAmbiguityForIncompleteQuery()
    {
        // Arrange
        var input = "tìm kiếm";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Ambiguity.IsAmbiguous.Should().BeTrue();
        result.Ambiguity.Suggestions.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("bao nhiêu khách hàng?")]
    [InlineData("top khách hàng?")]
    public async Task ShouldDetectAmbiguityWhenMissingDetails(string input)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Ambiguity.IsAmbiguous.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNotFlagAmbiguousWhenQueryIsComplete()
    {
        // Arrange
        var input = "Liệt kê 10 khách hàng có doanh thu cao nhất";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Ambiguity.IsAmbiguous.Should().BeFalse();
    }

    #endregion

    #region Language Detection Tests

    [Theory]
    [InlineData("Có bao nhiêu khách hàng?", "vi")]
    [InlineData("Tổng doanh thu theo tháng", "vi")]
    [InlineData("How many customers?", "en")]
    [InlineData("List all orders", "en")]
    public async Task ShouldDetectLanguage(string input, string expectedLang)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Language.Should().Be(expectedLang);
    }

    #endregion

    #region Normalization Tests

    [Fact]
    public async Task ShouldTrimAndNormalizeWhitespace()
    {
        // Arrange
        var input = "  Có   bao   nhiêu   khách   hàng?  ";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.NormalizedInput.Should().NotContain("  ");
    }

    [Fact]
    public async Task ShouldExpandVietnameseAbbreviations()
    {
        // Arrange
        var input = "ds khách hàng";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.NormalizedInput.Should().Contain("Customer");
    }

    [Fact]
    public async Task ShouldPreserveOriginalInput()
    {
        // Arrange
        var input = "  Có   bao   nhiêu   khách   hàng?  ";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.OriginalInput.Should().Be(input);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ShouldThrowWhenEmptyInput()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _task.ExecuteAsync("   "));
    }

    [Fact]
    public async Task ShouldHandleEmptyString()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _task.ExecuteAsync(""));
    }

    [Fact]
    public async Task ShouldReturnTimestamp()
    {
        // Arrange
        var input = "Liệt kê khách hàng";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Confidence Scoring Tests

    [Fact]
    public async Task ShouldReturnHighConfidenceForClearQueries()
    {
        // Arrange
        var input = "Có bao nhiêu khách hàng mua hàng trong tháng 1 năm 2024?";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Confidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task ShouldReturnLowerConfidenceForAmbiguousQueries()
    {
        // Arrange
        var input = "tìm";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.Confidence.Should().BeLessThan(0.7);
    }

    #endregion

    #region Warning Generation Tests

    [Fact]
    public async Task ShouldGenerateWarningsForRiskyOperations()
    {
        // Arrange
        var input = "Xóa tất cả đơn hàng cũ";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Warnings.Should().NotBeEmpty();
    }

    #endregion

    #region Entity Extraction Tests

    [Theory]
    [InlineData("Liệt kê khách hàng có đơn hàng trong bảng Orders")]
    public async Task ShouldExtractEntitiesFromQuery(string input)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Intent.DetectedEntities.Should().NotBeEmpty();
    }

    #endregion

    #region Vietnamese Mapping Tests

    [Theory]
    [InlineData("Tổng doanh thu", "SUM")]
    [InlineData("Số lượng đơn hàng", "COUNT")]
    public async Task ShouldMapVietnameseTermsToSqlFunctions(string input, string expectedTerm)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.NormalizedInput.Should().Contain(expectedTerm);
    }

    #endregion
}

internal sealed class StubIntentRoutingPromptService : IIntentRoutingPromptService
{
    public string? ClarificationResponse { get; set; }
    public string? RejectionResponse { get; set; }

    public Task<string?> GenerateClarificationAsync(
        string originalInput,
        string language,
        IReadOnlyList<string> ambiguityReasons,
        IReadOnlyList<string> existingSuggestions,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ClarificationResponse);
    }

    public Task<string?> GenerateRejectionMessageAsync(
        string originalInput,
        string language,
        IntentCategory intent,
        RiskLevel riskLevel,
        double confidence,
        IReadOnlyList<string> detectedEntities,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RejectionResponse);
    }
}
