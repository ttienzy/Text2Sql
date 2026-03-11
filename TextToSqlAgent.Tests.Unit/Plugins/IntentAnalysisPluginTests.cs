using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Plugins;
using TextToSqlAgent.Tests.Unit.Mocks;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.Plugins;

/// <summary>
/// P1-06: Refactored unit tests using mock LLM client (no real API calls)
/// </summary>
public class IntentAnalysisPluginTests
{
    private readonly MockLLMClient _mockLlm;
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
        _mockLlm = new MockLLMClient();
        _plugin = new IntentAnalysisPlugin(_mockLlm, NullLogger<IntentAnalysisPlugin>.Instance);
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
        // Arrange
        _mockLlm.SetDefaultResponse(@"{
            ""intent"": ""SCHEMA"",
            ""target"": ""TABLES"",
            ""filters"": [],
            ""metrics"": [],
            ""needsClarification"": false
        }");

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
        // Arrange
        var targetTable = expectedTarget;
        _mockLlm.SetDefaultResponse($@"{{
            ""intent"": ""COUNT"",
            ""target"": ""{targetTable}"",
            ""filters"": [],
            ""metrics"": [],
            ""needsClarification"": false
        }}");

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
        // Arrange
        var targetTable = expectedTarget;
        _mockLlm.SetDefaultResponse($@"{{
            ""intent"": ""LIST"",
            ""target"": ""{targetTable}"",
            ""filters"": [],
            ""metrics"": [],
            ""needsClarification"": false
        }}");

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
        _mockLlm.SetDefaultResponse(@"{
            ""intent"": ""AGGREGATE"",
            ""target"": ""Customers"",
            ""filters"": [],
            ""metrics"": [""COUNT"", ""SUM""],
            ""needsClarification"": false
        }");

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
        _mockLlm.SetDefaultResponse(@"{
            ""intent"": ""LIST"",
            ""target"": ""Customers"",
            ""filters"": [
                {
                    ""field"": ""City"",
                    ""operator"": ""="",
                    ""value"": ""Hà Nội""
                }
            ],
            ""metrics"": [],
            ""needsClarification"": false
        }");

        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.Intent.Should().Be(QueryIntent.LIST);
        result.Target.Should().Be("Customers");
        result.Filters.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Should_Handle_Ambiguous_Queries()
    {
        // Arrange
        var question = "Cho tôi thông tin";
        _mockLlm.SetDefaultResponse(@"{
            ""intent"": ""DETAIL"",
            ""target"": """",
            ""filters"": [],
            ""metrics"": [],
            ""needsClarification"": true,
            ""clarificationQuestion"": ""Bạn muốn xem thông tin về bảng nào?""
        }");

        // Act
        var result = await _plugin.AnalyzeIntentAsync(question, _testTables);

        // Assert
        result.NeedsClarification.Should().BeTrue();
        result.ClarificationQuestion.Should().NotBeNullOrEmpty();
    }
}