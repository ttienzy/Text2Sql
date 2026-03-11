using System.Text.Json;
using FluentAssertions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Tests.Unit.Core;

public class ToolInputTests
{
    [Fact]
    public void Get_Should_Deserialize_JsonElement_To_Typed_Object()
    {
        var json = JsonDocument.Parse("""
        {
          "relevantTables": [],
          "relevantRelationships": [],
          "tableColumns": {},
          "matches": []
        }
        """).RootElement.Clone();

        var input = new ToolInput
        {
            Parameters = new Dictionary<string, object>
            {
                ["schema_context"] = json
            }
        };

        var context = input.Get<RetrievedSchemaContext>("schema_context");

        context.Should().NotBeNull();
        context.RelevantTables.Should().BeEmpty();
        context.RelevantRelationships.Should().BeEmpty();
        context.TableColumns.Should().BeEmpty();
    }

    [Fact]
    public void GetString_Should_Resolve_From_Alias_Keys()
    {
        var input = new ToolInput
        {
            Parameters = new Dictionary<string, object>
            {
                ["query"] = "show all customers"
            }
        };

        var question = input.GetString("question", "query");

        question.Should().Be("show all customers");
    }

    [Fact]
    public void Get_Should_Parse_Primitive_Value_From_String()
    {
        var input = new ToolInput
        {
            Parameters = new Dictionary<string, object>
            {
                ["max_steps"] = "12"
            }
        };

        var maxSteps = input.Get<int>("max_steps");

        maxSteps.Should().Be(12);
    }
}
