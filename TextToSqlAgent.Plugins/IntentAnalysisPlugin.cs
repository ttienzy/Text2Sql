using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Plugins;

public class IntentAnalysisPlugin
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<IntentAnalysisPlugin> _logger;

    public IntentAnalysisPlugin(ILLMClient llmClient, ILogger<IntentAnalysisPlugin> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    [KernelFunction, Description("Analyze user intent from natural language question")]
    public async Task<IntentAnalysis> AnalyzeIntentAsync(
        string question,
        List<string> availableTables,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Agent] Analyzing user intent...");

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(question, availableTables);

        var response = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        _logger.LogDebug("[Agent] LLM Response: {Response}", response);

        // Clean response (remove markdown code blocks if present)
        var jsonResponse = CleanJsonResponse(response);

        try
        {
            var intent = JsonSerializer.Deserialize<IntentAnalysis>(
                jsonResponse,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (intent == null)
            {
                throw new InvalidOperationException("Failed to deserialize intent analysis");
            }

            _logger.LogInformation(
                "[Agent] Intent: {Intent}, Target: {Target}",
                intent.Intent,
                intent.Target);

            return intent;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[Agent] Failed to parse intent response: {Response}", jsonResponse);
            throw new InvalidOperationException("Failed to parse LLM response", ex);
        }
    }

    private string BuildSystemPrompt()
    {
        return @"You are an expert at analyzing database query intentions.

                Your task: Analyze user questions (in Vietnamese or English) and determine:
                1. Query type (intent)
                2. Target table or schema element
                3. Metrics requested
                4. Filter conditions
                5. Whether clarification is needed

                IMPORTANT: Respond with ONLY valid JSON, no explanation, no markdown.

                JSON Format:
                {
                  ""intent"": ""LIST|COUNT|AGGREGATE|DETAIL|SCHEMA"",
                  ""target"": ""TableName or TABLES or SCHEMA"",
                  ""metrics"": [],
                  ""filters"": [],
                  ""needsClarification"": false,
                  ""clarificationQuestion"": """"
                }

                Intent Types:
                - LIST: User wants to see a list of records
                - COUNT: User wants to count something
                - AGGREGATE: User wants sum, average, top N, etc.
                - DETAIL: User wants details about specific record(s)
                - SCHEMA: User asks about database structure (tables, columns)

                Examples:
                Q: ""Có bao nhiêu bảng trong database?""
                A: {""intent"":""SCHEMA"",""target"":""TABLES"",""metrics"":[],""filters"":[],""needsClarification"":false,""clarificationQuestion"":""""}

                Q: ""Có bao nhiêu khách hàng?""
                A: {""intent"":""COUNT"",""target"":""Customers"",""metrics"":[],""filters"":[],""needsClarification"":false,""clarificationQuestion"":""""}

                Q: ""Liệt kê tất cả khách hàng""
                A: {""intent"":""LIST"",""target"":""Customers"",""metrics"":[],""filters"":[],""needsClarification"":false,""clarificationQuestion"":""""}

                Q: ""Top 10 khách hàng mua nhiều nhất""
                A: {""intent"":""AGGREGATE"",""target"":""Customers"",""metrics"":[""TotalPurchase""],""filters"":[{""field"":""Limit"",""operator"":""TOP"",""value"":""10""}],""needsClarification"":false,""clarificationQuestion"":""""}

                Q: ""Khách hàng ở Hà Nội""
                A: {""intent"":""LIST"",""target"":""Customers"",""metrics"":[],""filters"":[{""field"":""City"",""operator"":""="",""value"":""Hà Nội""}],""needsClarification"":false,""clarificationQuestion"":""""}";
    }

    private string BuildUserPrompt(string question, List<string> availableTables)
    {
        return $@"Available Tables: {string.Join(", ", availableTables)}

User Question: ""{question}""

Analyze and respond with JSON only:";
    }

    private string CleanJsonResponse(string response)
    {
        // Remove markdown code blocks
        var cleaned = response
            .Replace("```json", "")
            .Replace("```", "")
            .Trim();

        return cleaned;
    }
}