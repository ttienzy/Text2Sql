using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Plugins;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.Plugins;

public class IntentAnalysisPluginTests
{
    private readonly IntentAnalysisPlugin _plugin;
    private readonly List<string> _testTables = new()
    {
        "Customers",
        "Orders",
        "Products",
        "Categories",
        "OrderDetails"
    };

    public IntentAnalysisPluginTests()
    {
        // Get API key from environment variable
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("GEMINI_API_KEY not set");

        var config = new GeminiConfig
        {
            ApiKey = apiKey,
            Model = "gemini-2.5-flash",
            Temperature = 0.1,
            MaxTokens = 2048
        };

        var geminiClient = new GeminiClient(config, NullLogger<GeminiClient>.Instance);
        _plugin = new IntentAnalysisPlugin(geminiClient, NullLogger<IntentAnalysisPlugin>.Instance);
    }

    [Theory]
    [InlineData("Có bao nhiêu bảng?", QueryIntent.SCHEMA, "TABLES")]
    [InlineData("Liệt kê các bảng trong database", QueryIntent.SCHEMA, "TABLES")]
    [InlineData("Cho tôi danh sách tất cả các bảng", QueryIntent.SCHEMA, "TABLES")]
    public async Task Should_Detect_Schema_Queries(
        string question,
        QueryIntent expectedIntent,
        string expectedTarget)
    {
        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.Intent.Should().Be(expectedIntent);
        result.Target.Should().Be(expectedTarget);
        result.NeedsClarification.Should().BeFalse();
    }

    [Theory]
    [InlineData("Có bao nhiêu khách hàng?", QueryIntent.COUNT, "Customers")]
    [InlineData("Đếm số đơn hàng", QueryIntent.COUNT, "Orders")]
    [InlineData("Có tất cả bao nhiêu sản phẩm?", QueryIntent.COUNT, "Products")]
    public async Task Should_Detect_Count_Queries(
        string question,
        QueryIntent expectedIntent,
        string expectedTarget)
    {
        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.Intent.Should().Be(expectedIntent);
        result.Target.Should().Be(expectedTarget);
    }

    [Theory]
    [InlineData("Liệt kê tất cả khách hàng", QueryIntent.LIST, "Customers")]
    [InlineData("Cho tôi danh sách sản phẩm", QueryIntent.LIST, "Products")]
    [InlineData("Hiển thị các đơn hàng", QueryIntent.LIST, "Orders")]
    public async Task Should_Detect_List_Queries(
        string question,
        QueryIntent expectedIntent,
        string expectedTarget)
    {
        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.Intent.Should().Be(expectedIntent);
        result.Target.Should().Be(expectedTarget);
    }

    [Fact]
    public async Task Should_Detect_Aggregate_Queries()
    {
        // Arrange
        var question = "Top 10 khách hàng mua nhiều nhất";

        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.Intent.Should().Be(QueryIntent.AGGREGATE);
        result.Target.Should().Be("Customers");
    }

    [Fact]
    public async Task Should_Detect_Filters()
    {
        // Arrange
        var question = "Khách hàng ở Hà Nội";

        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.Intent.Should().Be(QueryIntent.LIST);
        result.Target.Should().Be("Customers");
        result.Filters.Should().NotBeEmpty();
    }
}