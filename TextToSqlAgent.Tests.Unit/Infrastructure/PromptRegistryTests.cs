using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Tests.Unit.Infrastructure;

public class PromptRegistryTests
{
    [Fact]
    public void RegisterTemplate_Should_Normalize_Trailing_Dot_In_Path_Based_Name()
    {
        var registry = new PromptRegistry(NullLogger<PromptRegistry>.Instance);
        var template = new PromptTemplate
        {
            Name = "sql-generation/with-suggestions.",
            Version = "1.0.0",
            SystemPrompt = "system",
            UserPrompt = "user"
        };

        registry.RegisterTemplate(template);

        var resolved = registry.GetTemplate("sql-generation/with-suggestions");

        resolved.Name.Should().Be("sql-generation/with-suggestions");
        resolved.UserPrompt.Should().Be("user");
    }

    [Fact]
    public void GetTemplate_Should_Match_Path_Names_With_Mixed_Separators()
    {
        var registry = new PromptRegistry(NullLogger<PromptRegistry>.Instance);
        var template = new PromptTemplate
        {
            Name = @"db-explorer\column-analysis",
            Version = "1.0.0",
            SystemPrompt = "system",
            UserPrompt = "user"
        };

        registry.RegisterTemplate(template);

        var resolved = registry.GetTemplate("db-explorer/column-analysis");

        resolved.Name.Should().Be("db-explorer/column-analysis");
    }
}
