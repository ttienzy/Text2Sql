using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Application.Agent.Tools;

/// <summary>
/// Tool that decomposes a complex question into simpler sub-queries.
/// Uses the LLM to break down multi-part analytical questions.
/// </summary>
public class QueryDecompositionTool : IAgentTool
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<QueryDecompositionTool> _logger;

    public string Name => "QueryDecomposition";
    public string Description =>
        "Break down a complex analytical question into simpler sub-queries. " +
        "Use this when a question involves comparisons, multiple aggregations, or multi-step analysis " +
        "that cannot be answered with a single SQL query.";

    public QueryDecompositionTool(ILLMClient llmClient, ILogger<QueryDecompositionTool> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, WorkingMemory memory, CancellationToken ct)
    {
        try
        {
            var schemaHint = memory.SchemaContext != null
                ? $"Available tables: {string.Join(", ", memory.DiscoveredTables)}"
                : "No schema loaded yet.";

            var prompt = $"""
                You are a SQL query decomposition expert.
                
                Question: {input.Query}
                
                {schemaHint}
                
                Break this question into 2-4 simpler sub-questions that can each be answered with a single SQL query.
                For each sub-question, explain what it needs to find.
                
                Format:
                1. [Sub-question] - [What SQL needs to find]
                2. [Sub-question] - [What SQL needs to find]
                ...
                
                Then provide a strategy for combining the results.
                """;

            var systemPrompt = "You are a SQL query decomposition expert.";
            var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, prompt, ct);

            _logger.LogInformation("[QueryDecompositionTool] Decomposed into sub-queries");
            return ToolResult.Ok($"Decomposition:\n{response}", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QueryDecompositionTool] Failed to decompose query");
            return ToolResult.Fail($"Query decomposition failed: {ex.Message}");
        }
    }
}
