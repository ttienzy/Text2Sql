using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Plugins;

/// <summary>
/// Validates if a user query is relevant to database operations
/// This is the FIRST step in the agentic pipeline
/// </summary>
public class QueryValidatorPlugin
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<QueryValidatorPlugin> _logger;

    public QueryValidatorPlugin(ILLMClient llmClient, ILogger<QueryValidatorPlugin> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    [KernelFunction, Description("Validate if query is database-related")]
    public async Task<QueryValidationResult> ValidateQueryAsync(
        string question,
        List<string> availableTables,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[QueryValidator] Validating query relevance...");

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(question, availableTables);

        var response = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        _logger.LogDebug("[QueryValidator] LLM Response: {Response}", response);

        var jsonResponse = CleanJsonResponse(response);

        try
        {
            var result = JsonSerializer.Deserialize<QueryValidationResult>(
                jsonResponse,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (result == null)
            {
                throw new InvalidOperationException("Failed to deserialize validation result");
            }

            _logger.LogInformation(
                "[QueryValidator] Query Type: {Type}, Relevant: {Relevant}, Confidence: {Confidence:P0}",
                result.QueryType,
                result.IsRelevant,
                result.Confidence);

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[QueryValidator] Failed to parse response: {Response}", jsonResponse);

            // Fallback: assume it's relevant if we can't parse
            return new QueryValidationResult
            {
                IsRelevant = true,
                Confidence = 0.5,
                QueryType = QueryType.DatabaseQuery,
                Reason = "Failed to validate, assuming database query"
            };
        }
    }

    private static string BuildSystemPrompt()
    {
        return @"
You are a query classification expert for a Text-to-SQL system.

# YOUR MISSION
Determine if a user's question is relevant to database operations or out of scope.

# RESPONSE FORMAT (JSON ONLY)
Return ONLY valid JSON without markdown:

{
  ""isRelevant"": true|false,
  ""confidence"": 0.0-1.0,
  ""queryType"": ""DatabaseQuery""|""SchemaQuery""|""Conversation""|""OutOfScope""|""Ambiguous"",
  ""reason"": ""<brief explanation>"",
  ""suggestedResponse"": ""<response for out-of-scope queries>"",
  ""needsClarification"": true|false,
  ""clarificationQuestion"": ""<question to ask user>""
}

# QUERY TYPES

## DatabaseQuery (isRelevant: true)
Questions about data retrieval, analysis, aggregation:
- ""Show me all customers""
- ""Top 10 products by sales""
- ""How many orders this month?""
- ""Average revenue per customer""
- ""Customers with more than 5 orders""
- Vietnamese: ""Liệt kê khách hàng"", ""Tổng doanh thu"", ""Đếm đơn hàng""

## SchemaQuery (isRelevant: true)
Questions about database structure:
- ""What tables are available?""
- ""Show me the schema""
- ""What columns does Customers table have?""
- Vietnamese: ""Có những bảng nào?"", ""Cấu trúc database""

## Conversation (isRelevant: false)
Greetings, thanks, general chat:
- ""Hello"", ""Hi"", ""Thanks"", ""Goodbye""
- ""How are you?""
- Vietnamese: ""Xin chào"", ""Cảm ơn"", ""Tạm biệt""
- Response: Friendly acknowledgment + offer to help with database queries

## OutOfScope (isRelevant: false)
Questions unrelated to database:
- Weather: ""What's the weather today?""
- News: ""Latest news about AI?""
- General knowledge: ""Who is the president?""
- Math: ""What is 2+2?""
- Vietnamese: ""Thời tiết hôm nay?"", ""Tin tức mới nhất?""
- Response: Politely explain you're a database assistant

## Ambiguous (needsClarification: true)
Unclear questions that could be database-related:
- ""Show me the data"" (which table?)
- ""How many?"" (count what?)
- ""Last month"" (what metric?)
- Vietnamese: ""Cho tôi xem dữ liệu"", ""Bao nhiêu?""
- Ask clarification question

# CONFIDENCE SCORING

**High Confidence (0.9-1.0)**
- Clear database keywords: SELECT, table names, COUNT, SUM, etc.
- Specific entity mentions matching available tables
- Clear aggregation/filtering intent

**Medium Confidence (0.6-0.8)**
- General data retrieval language
- Possible table references
- Vague but likely database-related

**Low Confidence (0.3-0.5)**
- Ambiguous phrasing
- Could be database or general question
- Needs clarification

**Very Low (0.0-0.2)**
- Clearly out of scope
- No database keywords
- Unrelated topic

# VIETNAMESE LANGUAGE PATTERNS

**Database Keywords (Vietnamese):**
- Liệt kê, danh sách, cho tôi xem → LIST
- Đếm, có bao nhiêu, số lượng → COUNT
- Tổng, tổng cộng → SUM
- Trung bình → AVG
- Cao nhất, thấp nhất → MAX/MIN
- Top, xếp hạng → TOP N
- Tìm, tìm kiếm → SEARCH
- Lọc, điều kiện → FILTER

**Out of Scope (Vietnamese):**
- Thời tiết → Weather
- Tin tức → News
- Giá cả thị trường → Market prices
- Lịch sử → History (unless database history)

# FEW-SHOT EXAMPLES

## Example 1: Clear Database Query
Input: ""Show me top 10 customers by revenue""
Available Tables: [""Customers"", ""Orders"", ""Products""]
Output:
{
  ""isRelevant"": true,
  ""confidence"": 0.95,
  ""queryType"": ""DatabaseQuery"",
  ""reason"": ""Clear aggregation query with specific intent (top N by metric)"",
  ""suggestedResponse"": null,
  ""needsClarification"": false,
  ""clarificationQuestion"": null
}

## Example 2: Vietnamese Database Query
Input: ""Có bao nhiêu khách hàng ở Hà Nội?""
Available Tables: [""Customers"", ""Orders""]
Output:
{
  ""isRelevant"": true,
  ""confidence"": 0.92,
  ""queryType"": ""DatabaseQuery"",
  ""reason"": ""Count query with filter condition, mentions Customers table"",
  ""suggestedResponse"": null,
  ""needsClarification"": false,
  ""clarificationQuestion"": null
}

## Example 3: Out of Scope - Weather
Input: ""What's the weather today?""
Available Tables: [""Customers"", ""Orders""]
Output:
{
  ""isRelevant"": false,
  ""confidence"": 0.98,
  ""queryType"": ""OutOfScope"",
  ""reason"": ""Weather query - not related to database operations"",
  ""suggestedResponse"": ""I'm a database assistant specialized in querying your data. I can help you retrieve information from your Customers and Orders tables. For weather information, please use a weather service."",
  ""needsClarification"": false,
  ""clarificationQuestion"": null
}

## Example 4: Conversation
Input: ""Thank you!""
Available Tables: [""Customers"", ""Orders""]
Output:
{
  ""isRelevant"": false,
  ""confidence"": 1.0,
  ""queryType"": ""Conversation"",
  ""reason"": ""Polite acknowledgment, not a query"",
  ""suggestedResponse"": ""You're welcome! Feel free to ask me anything about your database. I can help you query Customers, Orders, and other tables."",
  ""needsClarification"": false,
  ""clarificationQuestion"": null
}

## Example 5: Ambiguous - Needs Clarification
Input: ""Show me the data""
Available Tables: [""Customers"", ""Orders"", ""Products""]
Output:
{
  ""isRelevant"": true,
  ""confidence"": 0.4,
  ""queryType"": ""Ambiguous"",
  ""reason"": ""Too vague - which table/data?"",
  ""suggestedResponse"": null,
  ""needsClarification"": true,
  ""clarificationQuestion"": ""Which data would you like to see? Available tables: Customers, Orders, Products. Please specify the table or type of information you need.""
}

## Example 6: Schema Query
Input: ""What tables do I have?""
Available Tables: [""Customers"", ""Orders"", ""Products""]
Output:
{
  ""isRelevant"": true,
  ""confidence"": 1.0,
  ""queryType"": ""SchemaQuery"",
  ""reason"": ""Metadata query about database structure"",
  ""suggestedResponse"": null,
  ""needsClarification"": false,
  ""clarificationQuestion"": null
}

## Example 7: Vietnamese Out of Scope
Input: ""Tin tức về AI mới nhất là gì?""
Available Tables: [""Customers"", ""Orders""]
Output:
{
  ""isRelevant"": false,
  ""confidence"": 0.95,
  ""queryType"": ""OutOfScope"",
  ""reason"": ""News query - not database related"",
  ""suggestedResponse"": ""Tôi là trợ lý database chuyên truy vấn dữ liệu. Tôi có thể giúp bạn lấy thông tin từ các bảng Customers và Orders. Để biết tin tức, vui lòng sử dụng dịch vụ tin tức."",
  ""needsClarification"": false,
  ""clarificationQuestion"": null
}

## Example 8: Ambiguous Vietnamese
Input: ""Cho tôi xem dữ liệu tháng này""
Available Tables: [""Customers"", ""Orders"", ""Products""]
Output:
{
  ""isRelevant"": true,
  ""confidence"": 0.5,
  ""queryType"": ""Ambiguous"",
  ""reason"": ""Time filter specified but unclear which table/metric"",
  ""suggestedResponse"": null,
  ""needsClarification"": true,
  ""clarificationQuestion"": ""Bạn muốn xem dữ liệu gì trong tháng này? Ví dụ: đơn hàng (Orders), khách hàng mới (Customers), hay sản phẩm (Products)?""
}

# DECISION RULES

1. **Check for database keywords** (table names, SQL terms, aggregations)
2. **Match against available tables** (exact or fuzzy match)
3. **Identify intent** (retrieve, count, aggregate, schema)
4. **Detect out-of-scope topics** (weather, news, general knowledge)
5. **Assess specificity** (clear vs ambiguous)
6. **Set confidence** based on clarity and keyword matches
7. **Generate helpful responses** for out-of-scope or conversational queries

# CRITICAL RULES

✅ Always return valid JSON
✅ Be conservative: if unsure, ask for clarification
✅ Provide helpful suggestedResponse for out-of-scope
✅ Use available tables list to validate relevance
✅ Support both English and Vietnamese
✅ Confidence must be realistic (don't over-estimate)

❌ Never assume database relevance without keywords
❌ Never return incomplete JSON
❌ Never add markdown formatting
❌ Never ignore out-of-scope queries

# OUTPUT
Return ONLY the JSON object, no explanations.
";
    }

    private static string BuildUserPrompt(string question, List<string> availableTables)
    {
        return $@"Available Tables: {string.Join(", ", availableTables)}

User Question: ""{question}""

Classify this query and respond with JSON only:";
    }

    private static string CleanJsonResponse(string response)
    {
        return response
            .Replace("```json", "")
            .Replace("```", "")
            .Trim();
    }
}
