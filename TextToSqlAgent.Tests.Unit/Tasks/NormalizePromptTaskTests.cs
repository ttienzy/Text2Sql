using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Tasks;
using Xunit;

namespace TextToSqlAgent.Tests.Unit.Tasks;

public class NormalizePromptTaskTests
{
    private readonly NormalizePromptTask _task;

    public NormalizePromptTaskTests()
    {
        _task = new NormalizePromptTask(NullLogger<NormalizePromptTask>.Instance);
    }

    [Theory]
    [InlineData("  Có bao nhiêu bảng  ", "Có bao nhiêu bảng")]
    [InlineData("   Liệt   kê   khách   hàng   ", "Liệt kê khách hàng")]
    public async Task Should_Trim_Whitespace(string input, string expected)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.NormalizedText.Should().Be(expected);
    }

    [Fact]
    public async Task Should_Expand_Abbreviations()
    {
        // Arrange
        var input = "Lấy ds các tb trong db";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.NormalizedText.Should().Contain("danh sách");
        result.NormalizedText.Should().Contain("bảng");
        result.NormalizedText.Should().Contain("database");
    }

    [Theory]
    [InlineData("Có bao nhiêu bảng trong database?", "vi")]
    [InlineData("How many tables in the database?", "en")]
    [InlineData("Khách hàng nào mua nhiều nhất", "vi")]
    public async Task Should_Detect_Language(string input, string expectedLang)
    {
        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.Language.Should().Be(expectedLang);
    }

    [Fact]
    public async Task Should_Preserve_Original_Prompt()
    {
        // Arrange
        var input = "  cho toi  ds  bang  ";

        // Act
        var result = await _task.ExecuteAsync(input);

        // Assert
        result.OriginalPrompt.Should().Be(input);
        result.NormalizedText.Should().NotBe(input);
    }

    [Fact]
    public async Task Should_Throw_When_Empty_Prompt()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _task.ExecuteAsync("   "));
    }
}