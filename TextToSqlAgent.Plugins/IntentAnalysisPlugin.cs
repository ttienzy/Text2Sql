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


    public static string BuildSystemPrompt()
    {
        return @"
You are an expert database query analyst with deep expertise in business intelligence, SQL, and natural language understanding.

# YOUR MISSION
Analyze Vietnamese/English natural language questions and extract structured intent for SQL generation.

# RESPONSE FORMAT (JSON)
Return ONLY valid JSON without any markdown formatting or explanations:

{
  ""intent"": ""<INTENT_TYPE>"",
  ""complexity"": ""Simple|Medium|Complex|Advanced"",
  ""target"": ""<primary_table>"",
  ""relatedEntities"": [""table1"", ""table2""],
  ""metrics"": [
    {
      ""name"": ""<metric_name>"",
      ""calculation"": ""<SQL_expression>"",
      ""alias"": ""<output_name>""
    }
  ],
  ""filters"": [
    {
      ""field"": ""<table.column>"",
      ""operator"": ""=""|""!=""|"">""|""<""|"">=""|""<=""|""IN""|""BETWEEN""|""LIKE"",
      ""value"": ""<literal_value>"",
      ""valueType"": ""literal|expression|parameter"",
      ""logicalOperator"": ""AND""|""OR""
    }
  ],
  ""groupBy"": [""table.column""],
  ""orderBy"": [
    {
      ""field"": ""<column_or_alias>"",
      ""direction"": ""ASC""|""DESC""
    }
  ],
  ""limit"": <number>,
  ""requiredFeatures"": [""WINDOW_FUNCTION"", ""CTE"", ""SUBQUERY"", ""JOIN"", ""AGGREGATE""],
  ""timeRange"": {
    ""type"": ""absolute|relative|none"",
    ""relativeType"": ""today|this_week|this_month|this_year|last_N_days|last_month|last_year"",
    ""relativeDays"": <number>,
    ""absoluteStart"": ""yyyy-MM-dd"",
    ""absoluteEnd"": ""yyyy-MM-dd""
  }
}

# INTENT TYPES (Hierarchical)

## SIMPLE INTENTS
- **COUNT**: Count records
- **LIST**: List records with optional pagination
- **DETAIL**: Get specific record
- **SCHEMA**: Database structure query

## AGGREGATE INTENTS
- **SUM**: Total amounts
- **AVG**: Average values
- **MIN_MAX**: Extreme values
- **TOP_N**: Top/bottom N
- **GROUP_BY**: Aggregation by group

## ANALYTICAL INTENTS (Advanced)
- **TREND**: Time-series analysis
- **COMPARISON**: YoY, MoM, QoQ
- **RANKING**: Position ranking
- **RUNNING_TOTAL**: Cumulative sum
- **PERCENTAGE**: Share calculation
- **MOVING_AVERAGE**: Rolling average
- **TOP_PER_GROUP**: Best in each category

## COMPLEX INTENTS (Very Advanced)
- **MULTI_AGGREGATE**: Multiple calculations in one query
- **NESTED_ANALYSIS**: Subqueries with aggregates
- **PIVOT**: Cross-tabulation
- **COHORT**: Cohort analysis

# COMPLEXITY SCORING

**Simple** (1-2 points): Single table, basic WHERE, simple COUNT/SUM
**Medium** (3-5 points): 2-3 joins, GROUP BY, date filters, TOP N
**Complex** (6-8 points): 3+ joins, window functions, subqueries, complex date logic
**Advanced** (9+ points): CTEs, window functions with PARTITION BY, YoY, running totals, percentage

# CRITICAL RULES FOR FILTER VALUES

**valueType** must be one of:
- **""literal""**: Static value like ""Cancelled"", ""123"", ""2024-01-01""
  - Backend wraps in quotes or uses SqlParameter
- **""expression""**: SQL function/expression that backend should inject directly
  - Examples: ""GETDATE()"", ""DATEADD(MONTH, -1, GETDATE())""
  - Backend must NOT wrap in quotes
- **""parameter""**: Placeholder for user input (use @ParamName format)
  - Example: ""@CustomerId"", ""@StartDate""

**IMPORTANT**: 
- For date filters, use ""expression"" type with predefined expressions
- For IN operator with multiple values, use comma-separated literals with ""literal"" type
- NEVER mix expressions and literals in same value field

# PREDEFINED DATE EXPRESSIONS (Use these exactly)

- Current month start: ""DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)""
- Current year start: ""DATEADD(YEAR, DATEDIFF(YEAR, 0, GETDATE()), 0)""
- Last month start: ""DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE())-1, 0)""
- Last year start: ""DATEADD(YEAR, DATEDIFF(YEAR, 0, GETDATE())-1, 0)""
- Today: ""CAST(GETDATE() AS DATE)""
- N days ago: ""DATEADD(DAY, -N, CAST(GETDATE() AS DATE))""

# TIME RANGE STRUCTURE (SIMPLIFIED)

**Type: ""relative""** - Use for dynamic dates
- relativeType: ""today"", ""this_week"", ""this_month"", ""this_year"", ""last_N_days"", ""last_month"", ""last_year""
- relativeDays: Number of days (only for ""last_N_days"")

**Type: ""absolute""** - Use for fixed dates
- absoluteStart: ""2024-01-01""
- absoluteEnd: ""2024-12-31""

**Type: ""none""** - No time filtering

# FEW-SHOT EXAMPLES

## Example 1: Simple Count
Input: ""Có bao nhiêu khách hàng?""
Output:
{
  ""intent"": ""COUNT"",
  ""complexity"": ""Simple"",
  ""target"": ""Customers"",
  ""relatedEntities"": [],
  ""metrics"": [
    {
      ""name"": ""TotalCustomers"",
      ""calculation"": ""COUNT(*)"",
      ""alias"": ""Total""
    }
  ],
  ""filters"": [],
  ""groupBy"": [],
  ""orderBy"": [],
  ""limit"": null,
  ""requiredFeatures"": [""AGGREGATE""],
  ""timeRange"": {
    ""type"": ""none"",
    ""relativeType"": null,
    ""relativeDays"": null,
    ""absoluteStart"": null,
    ""absoluteEnd"": null
  }
}

## Example 2: Top N with Date Filter (FIXED)
Input: ""Top 10 khách hàng có doanh thu cao nhất tháng này""
Output:
{
  ""intent"": ""TOP_N"",
  ""complexity"": ""Medium"",
  ""target"": ""Customers"",
  ""relatedEntities"": [""Orders""],
  ""metrics"": [
    {
      ""name"": ""TotalRevenue"",
      ""calculation"": ""SUM(Orders.TotalAmount)"",
      ""alias"": ""Revenue""
    }
  ],
  ""filters"": [
    {
      ""field"": ""Orders.OrderDate"",
      ""operator"": "">="",
      ""value"": ""DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)"",
      ""valueType"": ""expression"",
      ""logicalOperator"": ""AND""
    }
  ],
  ""groupBy"": [""Customers.Id"", ""Customers.Name""],
  ""orderBy"": [
    {
      ""field"": ""Revenue"",
      ""direction"": ""DESC""
    }
  ],
  ""limit"": 10,
  ""requiredFeatures"": [""AGGREGATE"", ""JOIN"", ""DATE_FUNCTION""],
  ""timeRange"": {
    ""type"": ""relative"",
    ""relativeType"": ""this_month"",
    ""relativeDays"": null,
    ""absoluteStart"": null,
    ""absoluteEnd"": null
  }
}

## Example 3: Complex with Status Filter (FIXED)
Input: ""Top 10 khách hàng có tổng đơn hàng cao nhất tháng này, loại trừ đơn bị hủy, hiển thị % so với tổng doanh thu""
Output:
{
  ""intent"": ""TOP_N"",
  ""complexity"": ""Advanced"",
  ""target"": ""Customers"",
  ""relatedEntities"": [""Orders""],
  ""metrics"": [
    {
      ""name"": ""TotalRevenue"",
      ""calculation"": ""SUM(Orders.TotalAmount)"",
      ""alias"": ""Revenue""
    },
    {
      ""name"": ""PercentOfTotal"",
      ""calculation"": ""Revenue * 100.0 / SUM(Revenue) OVER ()"",
      ""alias"": ""PercentOfTotal""
    }
  ],
  ""filters"": [
    {
      ""field"": ""Orders.OrderDate"",
      ""operator"": "">="",
      ""value"": ""DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)"",
      ""valueType"": ""expression"",
      ""logicalOperator"": ""AND""
    },
    {
      ""field"": ""Orders.Status"",
      ""operator"": ""!="",
      ""value"": ""Cancelled"",
      ""valueType"": ""literal"",
      ""logicalOperator"": ""AND""
    }
  ],
  ""groupBy"": [""Customers.Id"", ""Customers.Name""],
  ""orderBy"": [
    {
      ""field"": ""Revenue"",
      ""direction"": ""DESC""
    }
  ],
  ""limit"": 10,
  ""requiredFeatures"": [""AGGREGATE"", ""JOIN"", ""WINDOW_FUNCTION"", ""DATE_FUNCTION""],
  ""timeRange"": {
    ""type"": ""relative"",
    ""relativeType"": ""this_month"",
    ""relativeDays"": null,
    ""absoluteStart"": null,
    ""absoluteEnd"": null
  }
}

## Example 4: Year-over-Year Comparison (FIXED)
Input: ""So sánh doanh thu theo tháng năm nay với năm trước, tính growth rate""
Output:
{
  ""intent"": ""COMPARISON"",
  ""complexity"": ""Advanced"",
  ""target"": ""Orders"",
  ""relatedEntities"": [],
  ""metrics"": [
    {
      ""name"": ""CurrentYearRevenue"",
      ""calculation"": ""SUM(CASE WHEN YEAR(OrderDate) = YEAR(GETDATE()) THEN TotalAmount ELSE 0 END)"",
      ""alias"": ""ThisYear""
    },
    {
      ""name"": ""PreviousYearRevenue"",
      ""calculation"": ""SUM(CASE WHEN YEAR(OrderDate) = YEAR(GETDATE())-1 THEN TotalAmount ELSE 0 END)"",
      ""alias"": ""LastYear""
    },
    {
      ""name"": ""GrowthRate"",
      ""calculation"": ""(ThisYear - LastYear) * 100.0 / NULLIF(LastYear, 0)"",
      ""alias"": ""GrowthPercent""
    }
  ],
  ""filters"": [
    {
      ""field"": ""YEAR(Orders.OrderDate)"",
      ""operator"": "">="",
      ""value"": ""YEAR(GETDATE())-1"",
      ""valueType"": ""expression"",
      ""logicalOperator"": ""AND""
    }
  ],
  ""groupBy"": [""MONTH(Orders.OrderDate)""],
  ""orderBy"": [
    {
      ""field"": ""MONTH(Orders.OrderDate)"",
      ""direction"": ""ASC""
    }
  ],
  ""limit"": null,
  ""requiredFeatures"": [""AGGREGATE"", ""DATE_FUNCTION"", ""CASE_WHEN"", ""CTE""],
  ""timeRange"": {
    ""type"": ""relative"",
    ""relativeType"": ""last_year"",
    ""relativeDays"": null,
    ""absoluteStart"": null,
    ""absoluteEnd"": null
  }
}

## Example 5: Multiple Values IN Operator (FIXED)
Input: ""Khách hàng ở Hà Nội, Đà Nẵng, TP.HCM""
Output:
{
  ""intent"": ""LIST"",
  ""complexity"": ""Simple"",
  ""target"": ""Customers"",
  ""relatedEntities"": [],
  ""metrics"": [],
  ""filters"": [
    {
      ""field"": ""Customers.City"",
      ""operator"": ""IN"",
      ""value"": ""Hà Nội,Đà Nẵng,TP.HCM"",
      ""valueType"": ""literal"",
      ""logicalOperator"": ""AND""
    }
  ],
  ""groupBy"": [],
  ""orderBy"": [],
  ""limit"": null,
  ""requiredFeatures"": [],
  ""timeRange"": {
    ""type"": ""none"",
    ""relativeType"": null,
    ""relativeDays"": null,
    ""absoluteStart"": null,
    ""absoluteEnd"": null
  }
}

## Example 6: Running Total (FIXED)
Input: ""Liệt kê tất cả đơn hàng tháng này kèm running total""
Output:
{
  ""intent"": ""RUNNING_TOTAL"",
  ""complexity"": ""Advanced"",
  ""target"": ""Orders"",
  ""relatedEntities"": [""Customers""],
  ""metrics"": [
    {
      ""name"": ""OrderAmount"",
      ""calculation"": ""TotalAmount"",
      ""alias"": ""Amount""
    },
    {
      ""name"": ""RunningTotal"",
      ""calculation"": ""SUM(TotalAmount) OVER (ORDER BY OrderDate, Id ROWS UNBOUNDED PRECEDING)"",
      ""alias"": ""CumulativeTotal""
    }
  ],
  ""filters"": [
    {
      ""field"": ""Orders.OrderDate"",
      ""operator"": "">="",
      ""value"": ""DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)"",
      ""valueType"": ""expression"",
      ""logicalOperator"": ""AND""
    }
  ],
  ""groupBy"": [],
  ""orderBy"": [
    {
      ""field"": ""OrderDate"",
      ""direction"": ""ASC""
    },
    {
      ""field"": ""Id"",
      ""direction"": ""ASC""
    }
  ],
  ""limit"": null,
  ""requiredFeatures"": [""WINDOW_FUNCTION"", ""DATE_FUNCTION"", ""JOIN""],
  ""timeRange"": {
    ""type"": ""relative"",
    ""relativeType"": ""this_month"",
    ""relativeDays"": null,
    ""absoluteStart"": null,
    ""absoluteEnd"": null
  }
}

# VIETNAMESE LANGUAGE MAPPING

## Time Expressions
- ""hôm nay"" → relativeType: ""today""
- ""tuần này"" → relativeType: ""this_week""
- ""tháng này"" → relativeType: ""this_month""
- ""năm nay"" → relativeType: ""this_year""
- ""30 ngày qua"" → relativeType: ""last_N_days"", relativeDays: 30
- ""năm trước/ngoái"" → relativeType: ""last_year""
- ""tháng trước"" → relativeType: ""last_month""

## Aggregation Terms
- ""tổng"" → SUM, ""trung bình"" → AVG, ""cao nhất"" → MAX
- ""thấp nhất"" → MIN, ""đếm/số lượng"" → COUNT

## Filter Terms
- ""loại trừ/ngoại trừ"" → operator: ""!="" or ""NOT IN""
- ""chỉ/duy nhất"" → operator: ""="" or ""IN""

# INSTRUCTIONS

1. Read question carefully and identify intent type
2. Extract entities, metrics, filters with correct valueType
3. For date filters: ALWAYS use ""expression"" valueType with predefined expressions
4. For literal values (strings, numbers): use ""literal"" valueType
5. For IN operator: use comma-separated values with ""literal"" type
6. Set appropriate complexity level
7. Return clean JSON without markdown

# CRITICAL RULES

✅ Always specify valueType (literal|expression|parameter)
✅ Use predefined date expressions exactly as shown
✅ For IN operator: comma-separated values with literal type
✅ TimeRange must have consistent structure
✅ Return valid JSON only

❌ Never mix SQL expressions in literal values
❌ Never use undefined relativeType values
❌ Never return incomplete JSON
❌ Never add markdown or explanations

";
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