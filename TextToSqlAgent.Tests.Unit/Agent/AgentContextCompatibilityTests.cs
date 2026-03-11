using FluentAssertions;
using TextToSqlAgent.Core.Agent;

namespace TextToSqlAgent.Tests.Unit.Agent;

public class AgentContextCompatibilityTests
{
    [Fact]
    public void ToResult_Should_Map_Legacy_QueryResults_Key()
    {
        var context = new AgentContext(new AgentRequest("test"));
        context.State.Status = "Complete";

        var expectedResult = new { rows = 5 };
        context.WorkingMemory["query_results"] = expectedResult;

        var result = context.ToResult();

        result.Success.Should().BeTrue();
        result.QueryResult.Should().Be(expectedResult);
    }
}
