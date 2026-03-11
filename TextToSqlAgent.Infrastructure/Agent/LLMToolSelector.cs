using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tools;

namespace TextToSqlAgent.Infrastructure.Agent;

/// <summary>
/// LLM-based tool selector for ReAct Agent
/// Uses LLM to intelligently select tools and extract parameters
/// </summary>
public class LLMToolSelector
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<LLMToolSelector> _logger;

    public LLMToolSelector(
        ILLMClient llmClient,
        ILogger<LLMToolSelector> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<AgentAction?> SelectActionAsync(
        string thought,
        string plan,
        AgentContext context,
        List<ITool> availableTools,
        CancellationToken ct)
    {
        _logger.LogDebug("[LLMToolSelector] Selecting action from plan...");

        // Build tool descriptions
        var toolDescriptions = string.Join("\n", availableTools.Select(t =>
            $"- {t.Name}: {t.Description}"));

        // Build context summary from step history
        var previousSteps = context.State.History.Count > 0
            ? string.Join("\n", context.State.History.Select(s =>
                $"Step {s.StepNumber}: {s.Action?.ToolName ?? "thinking"} → {(s.Observation?.Success == true ? "Success" : "Failed")}"))
            : "No previous steps";

        var prompt = BuildSelectionPrompt(
            context.Request.Question,
            thought,
            plan,
            toolDescriptions,
            previousSteps,
            context.WorkingMemory);

        try
        {
            var response = await _llmClient.CompleteAsync(prompt, ct);
            var cleanedResponse = CleanJsonResponse(response);

            var action = JsonSerializer.Deserialize<AgentAction>(
                cleanedResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (action == null)
            {
                _logger.LogWarning("[LLMToolSelector] Failed to parse action from LLM response");
                return null;
            }

            _logger.LogInformation(
                "[LLMToolSelector] Selected tool: {Tool} with {ParamCount} parameters",
                action.ToolName,
                action.Parameters.Count);

            return action;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[LLMToolSelector] Failed to parse LLM response as JSON");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LLMToolSelector] Error selecting action");
            return null;
        }
    }

    private static string BuildSelectionPrompt(
        string userQuestion,
        string thought,
        string plan,
        string toolDescriptions,
        string previousSteps,
        Dictionary<string, object> workingMemory)
    {
        var memoryContext = workingMemory.Count > 0
            ? string.Join("\n", workingMemory.Select(kvp => $"- {kvp.Key}: {kvp.Value}"))
            : "Empty";

        return $@"You are an AI agent selecting the next tool to use.

# USER QUESTION
{userQuestion}

# YOUR REASONING
Thought: {thought}
Plan: {plan}

# AVAILABLE TOOLS
{toolDescriptions}

# PREVIOUS STEPS
{previousSteps}

# WORKING MEMORY
{memoryContext}

# YOUR TASK
Based on your reasoning and plan, select the BEST tool to use next and extract the required parameters.

# DECISION RULES
1. If you need to understand database schema → use ""explore_schema""
2. If you have schema and need to generate SQL → use ""generate_sql""
3. If you have SQL and need to execute it → use ""execute_sql""
4. If you need to validate SQL syntax → use ""validate_sql""
5. If query is complex and needs decomposition → use ""decompose_query""
6. If query is ambiguous → use ""detect_ambiguity""
7. If you need to analyze complexity → use ""analyze_complexity""
8. If you need to verify results → use ""verify_result""
9. If task is complete and you have the answer → use ""finish""

# PARAMETER EXTRACTION
- For ""explore_schema"": {{""query"": ""<user question>""}}
- For ""generate_sql"": {{""question"": ""<user question>"", ""schema_context"": ""<from working memory>""}}
- For ""execute_sql"": {{""sql"": ""<from working memory>""}}
- For ""validate_sql"": {{""sql"": ""<sql to validate>""}}
- For ""decompose_query"": {{""question"": ""<complex query>""}}
- For ""detect_ambiguity"": {{""question"": ""<user question>"", ""schema_context"": ""<from working memory>""}}
- For ""analyze_complexity"": {{""sql"": ""<sql to analyze>""}}
- For ""verify_result"": {{""question"": ""<user question>"", ""sql"": ""<sql from working memory>"", ""execution_result"": ""<from working memory>""}}
- For ""finish"": {{}}

# OUTPUT FORMAT (JSON ONLY)
Return ONLY valid JSON without markdown:

{{
  ""toolName"": ""<tool_name>"",
  ""parameters"": {{
    ""param1"": ""value1"",
    ""param2"": ""value2""
  }},
  ""reasoning"": ""<brief explanation why this tool>""
}}

# EXAMPLES

Example 1: Need schema information
{{
  ""toolName"": ""explore_schema"",
  ""parameters"": {{
    ""query"": ""Show me all customers""
  }},
  ""reasoning"": ""Need to explore database schema to find customer-related tables""
}}

Example 2: Have schema, need SQL
{{
  ""toolName"": ""generate_sql"",
  ""parameters"": {{
    ""question"": ""Show me all customers"",
    ""schema_context"": ""Customers table with Id, Name, Email columns""
  }},
  ""reasoning"": ""Have schema context, now generate SQL query""
}}

Example 3: Have SQL, need to execute
{{
  ""toolName"": ""execute_sql"",
  ""parameters"": {{
    ""sql"": ""SELECT * FROM Customers""
  }},
  ""reasoning"": ""SQL is ready, execute it to get results""
}}

Example 4: Task complete
{{
  ""toolName"": ""finish"",
  ""parameters"": {{}},
  ""reasoning"": ""Query executed successfully, have results to return""
}}

# CRITICAL RULES
✅ Return ONLY JSON, no explanations
✅ Use exact tool names from available tools
✅ Extract parameters from context when available
✅ If unsure, select ""explore_schema"" to gather information
✅ Always provide reasoning

❌ Never return markdown formatting
❌ Never use tools not in the available list
❌ Never leave parameters empty if they're required

Now select the best tool and return JSON:";
    }

    private static string CleanJsonResponse(string response)
    {
        return response
            .Replace("```json", "")
            .Replace("```", "")
            .Trim();
    }
}
