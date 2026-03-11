using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Agent;

namespace TextToSqlAgent.Tests.Unit.Agent;

public class ReActAgentStateIsolationTests
{
    private readonly ReActAgent _agent;

    public ReActAgentStateIsolationTests()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var selector = new LLMToolSelector(new FinishActionLlmClient(), NullLogger<LLMToolSelector>.Instance);

        _agent = new ReActAgent(
            registry,
            new DeterministicReasoningEngine(),
            new TerminatingReflectionEngine(),
            selector,
            NullLogger<ReActAgent>.Instance);
    }

    [Fact]
    public async Task RunAsync_Should_NotReuseState_BetweenSequentialRequests()
    {
        var firstResult = await _agent.RunAsync(new AgentRequest("question-1"));
        var secondResult = await _agent.RunAsync(new AgentRequest("question-2"));

        firstResult.Success.Should().BeTrue();
        secondResult.Success.Should().BeTrue();

        firstResult.TotalSteps.Should().Be(1);
        secondResult.TotalSteps.Should().Be(1);
        secondResult.Steps.Single().StepNumber.Should().Be(1);
        secondResult.Steps.Single().Thought.Should().Contain("question-2");
    }

    [Fact]
    public async Task RunAsync_Should_IsolateState_UnderConcurrentRequests()
    {
        var workloads = Enumerable.Range(1, 20)
            .Select(i =>
            {
                var question = $"question-{i}";
                return (question, task: _agent.RunAsync(new AgentRequest(question)));
            })
            .ToList();

        await Task.WhenAll(workloads.Select(w => w.task));

        foreach (var workload in workloads)
        {
            var result = workload.task.Result;

            result.Success.Should().BeTrue();
            result.TotalSteps.Should().Be(1);
            result.Steps.Should().ContainSingle();
            result.Steps[0].StepNumber.Should().Be(1);
            result.Steps[0].Thought.Should().Contain(workload.question);
        }
    }

    private sealed class DeterministicReasoningEngine : IReasoningEngine
    {
        public async Task<(string Thought, string Plan)> ThinkAsync(AgentContext context, CancellationToken ct = default)
        {
            await Task.Delay(5, ct);
            return ($"think:{context.Request.Question}", "finish");
        }
    }

    private sealed class TerminatingReflectionEngine : IReflectionEngine
    {
        public async Task<AgentReflection> ReflectAsync(
            AgentContext context,
            AgentObservation observation,
            CancellationToken ct = default)
        {
            await Task.Delay(5, ct);
            return new AgentReflection
            {
                Assessment = "done",
                ShouldTerminate = true,
                TerminationReason = "Task complete",
                Confidence = 1.0
            };
        }
    }

    private sealed class FinishActionLlmClient : ILLMClient
    {
        private const string FinishActionResponse = """{"toolName":"finish","parameters":{},"reasoning":"done"}""";

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FinishActionResponse);
        }

        public Task<string> CompleteWithSystemPromptAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FinishActionResponse);
        }
    }
}
