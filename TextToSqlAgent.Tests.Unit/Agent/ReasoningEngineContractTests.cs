using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Agent;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Tests.Unit.Agent;

public class ReasoningEngineContractTests
{
    [Fact]
    public async Task ThinkAsync_Should_Parse_Json_Thought_And_Plan()
    {
        var engine = CreateEngine("""{"thought":"Need schema context","plan":"Explore schema for relevant tables"}""");
        var context = new AgentContext(new AgentRequest("show customers"));

        var (thought, plan) = await engine.ThinkAsync(context);

        thought.Should().Be("Need schema context");
        plan.Should().Be("Explore schema for relevant tables");
    }

    [Fact]
    public async Task ThinkAsync_Should_Parse_Legacy_Thought_And_Plan_Format()
    {
        var engine = CreateEngine("THOUGHT: I should inspect schema first\nPLAN: Use schema exploration");
        var context = new AgentContext(new AgentRequest("show customers"));

        var (thought, plan) = await engine.ThinkAsync(context);

        thought.Should().Be("I should inspect schema first");
        plan.Should().Be("Use schema exploration");
    }

    [Fact]
    public async Task ThinkAsync_Should_Use_Action_As_Plan_When_Plan_Is_Missing()
    {
        var engine = CreateEngine("""{"thought":"Need to gather context","action":"explore_schema"}""");
        var context = new AgentContext(new AgentRequest("show customers"));

        var (thought, plan) = await engine.ThinkAsync(context);

        thought.Should().Be("Need to gather context");
        plan.Should().Be("explore_schema");
    }

    private static ReasoningEngine CreateEngine(string llmResponse)
    {
        var toolRegistry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var promptRegistry = new PromptRegistry(NullLogger<PromptRegistry>.Instance);

        return new ReasoningEngine(
            new StubLlmClient(llmResponse),
            toolRegistry,
            promptRegistry,
            NullLogger<ReasoningEngine>.Instance);
    }

    private sealed class StubLlmClient : ILLMClient
    {
        private readonly string _response;

        public StubLlmClient(string response)
        {
            _response = response;
        }

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }

        public Task<string> CompleteWithSystemPromptAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }
    }
}
