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

  [KernelFunction, Description("Analyze user query and extract structured intent for SQL generation")]
  public async Task<IntentAnalysis> AnalyzeIntentAsync(
      [Description("User's natural language query")] string userQuery,
      [Description("Available database tables")] List<string> availableTables,
      CancellationToken cancellationToken = default)
  {
    try
    {
      var systemPrompt = BuildSystemPrompt();
      var schemaContext = string.Join(", ", availableTables);
      var userPrompt = BuildUserPrompt(userQuery, schemaContext);

      var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, userPrompt, cancellationToken);

      // Clean the JSON response
      var cleanedResponse = response
          .Replace("```json", "")
          .Replace("```", "")
          .Trim();

      // Deserialize the JSON response to IntentAnalysis object
      var options = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      };

      IntentAnalysis intentAnalysis;
      try
      {
        // ✅ FIX: Normalize intent value before deserialization
        var normalizedResponse = NormalizeIntentInJson(cleanedResponse);

        intentAnalysis = JsonSerializer.Deserialize<IntentAnalysis>(normalizedResponse, options)
            ?? throw new InvalidOperationException("Failed to deserialize intent analysis response");
      }
      catch (JsonException ex)
      {
        _logger.LogWarning(ex,
            "[IntentAnalysis] Failed to deserialize LLM response, using fallback. Response preview: {Preview}",
            cleanedResponse.Substring(0, Math.Min(200, cleanedResponse.Length)));

        // ✅ FALLBACK: Return safe defaults for complex/ambiguous queries
        intentAnalysis = new IntentAnalysis
        {
          Intent = QueryIntent.MULTI_AGGREGATE, // Generic fallback for complex queries
          Complexity = "Complex",
          Target = string.Empty,
          NeedsClarification = true,
          ClarificationQuestion = "I understand you want to analyze data, but could you rephrase your question to be more specific? For example, specify which tables, time periods, or calculations you need."
        };

        _logger.LogInformation("[IntentAnalysis] Using fallback intent: {Intent}", intentAnalysis.Intent);
      }

      _logger.LogInformation("Intent analysis completed for query: {Query}", userQuery);
      return intentAnalysis;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error analyzing intent for query: {Query}", userQuery);
      throw;
    }
  }

  public static string BuildSystemPrompt()
  {
    return @"You are an expert database query analyst with deep expertise in business intelligence, SQL, and natural language understanding.

# YOUR MISSION
Analyze Vietnamese/English natural language questions and extract structured intent for SQL generation.

# RESPONSE FORMAT (JSON)
Return ONLY valid JSON without any markdown formatting or explanations:

{
  ""intent"": ""<INTENT_TYPE>"",
  ""complexity"": ""Simple|Medium|Complex|Advanced"",
  ""target"": ""<primary_table>"",
  ""relatedEntities"": [""table1"", ""table2""],
  ""selectColumns"": [""table.column1"", ""table.column2""],
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

## CRITICAL: Use ONLY these exact values for ""intent"" field (case-sensitive):

### SIMPLE INTENTS
- **COUNT**: Count records
- **LIST**: List records with optional pagination
- **DETAIL**: Get specific record
- **SCHEMA**: Database structure query

### AGGREGATE INTENTS
- **SUM**: Total amounts
- **AVG**: Average values
- **MIN_MAX**: Extreme values
- **TOP_N**: Top/bottom N
- **GROUP_BY**: Aggregation by group
- **AGGREGATE**: Generic aggregation (use when unsure)

### ANALYTICAL INTENTS (Advanced)
- **TREND**: Time-series analysis
- **COMPARISON**: YoY, MoM, QoQ
- **RANKING**: Position ranking
- **RUNNING_TOTAL**: Cumulative sum
- **PERCENTAGE**: Share calculation
- **MOVING_AVERAGE**: Rolling average
- **TOP_PER_GROUP**: Best in each category

### COMPLEX INTENTS (Very Advanced)
- **MULTI_AGGREGATE**: Multiple calculations in one query
- **NESTED_ANALYSIS**: Subqueries with aggregates
- **PIVOT**: Cross-tabulation
- **COHORT**: Cohort analysis

## INTENT SELECTION RULES

✅ DO:
- Use MULTI_AGGREGATE for complex calculations, predictions, or multiple metrics
- Use AGGREGATE when unsure between specific aggregate types
- Use TREND for time-series or forecasting queries
- Use COMPARISON for period-over-period analysis

❌ DO NOT:
- Invent new intent types (e.g., ""PREDICTION"", ""FORECAST"", ""CALCULATION"")
- Use descriptive names (e.g., ""CUSTOMER_LIFETIME_VALUE"")
- Use generic terms (e.g., ""AGGREGATION"" - use ""AGGREGATE"" instead)

# COMPLEXITY SCORING

**Simple** (1-2 points): Single table, basic WHERE, simple COUNT/SUM
**Medium** (3-5 points): 2-3 joins, GROUP BY, date filters, TOP N
**Complex** (6-8 points): 3+ joins, window functions, subqueries, complex date logic
**Advanced** (9+ points): CTEs, window functions with PARTITION BY, YoY, running totals, percentage

# VIETNAMESE LANGUAGE MAPPING

Map Vietnamese business terms to SQL:

| Vietnamese | SQL Equivalent |
|------------|----------------|
| tháng này | MONTH(GETDATE()) |
| năm nay | YEAR(GETDATE()) |
| hôm nay | CAST(GETDATE() AS DATE) |
| tuần này | DATEPART(WEEK, GETDATE()) |
| quý này | DATEPART(QUARTER, GETDATE()) |
| 30 ngày qua | DATEADD(DAY, -30, GETDATE()) |
| top 10 | TOP 10 |
| cao nhất | ORDER BY ... DESC |
| thấp nhất | ORDER BY ... ASC |
| trung bình | AVG(...) |
| tổng | SUM(...) |
| đếm | COUNT(...) |
| phần trăm / % | * 100.0 / (cast to DECIMAL) |
| tăng trưởng | (Current - Previous) / Previous * 100 |
| so sánh | CASE WHEN or JOIN |
| loại trừ | WHERE ... NOT IN or != |
| bao gồm | WHERE ... IN or = |
| xếp hạng | ROW_NUMBER() / RANK() |
| running total | SUM() OVER (...) |

**CRITICAL**: Always use N prefix for Vietnamese strings: N'Nguyễn Văn A'

# INSTRUCTIONS

1. Read question carefully and identify intent type from the list above
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
✅ Use ONLY the intent types listed above (no custom types)

❌ Never mix SQL expressions in literal values
❌ Never use undefined relativeType values
❌ Never return incomplete JSON
❌ Never add markdown or explanations
❌ Never invent new intent types";
  }

  /// <summary>
  /// Normalize common LLM intent mistakes to valid enum values
  /// </summary>
  private string NormalizeIntentInJson(string jsonResponse)
  {
    try
    {
      // Parse JSON to extract intent value
      using var doc = JsonDocument.Parse(jsonResponse);
      if (doc.RootElement.TryGetProperty("intent", out var intentElement))
      {
        var rawIntent = intentElement.GetString();
        if (!string.IsNullOrEmpty(rawIntent))
        {
          var normalizedIntent = NormalizeIntentValue(rawIntent);
          if (normalizedIntent != rawIntent)
          {
            // Replace the intent value in JSON
            jsonResponse = jsonResponse.Replace(
                $"\"intent\": \"{rawIntent}\"",
                $"\"intent\": \"{normalizedIntent}\"");

            _logger.LogInformation(
                "[IntentAnalysis] Normalized intent '{Raw}' → '{Normalized}'",
                rawIntent, normalizedIntent);
          }
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "[IntentAnalysis] Failed to normalize intent in JSON, using original");
    }

    return jsonResponse;
  }

  /// <summary>
  /// Map common LLM mistakes to valid QueryIntent enum values
  /// </summary>
  private string NormalizeIntentValue(string rawIntent)
  {
    // Map common LLM mistakes to valid enum values
    var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      // Generic terms
      { "AGGREGATION", "AGGREGATE" },
      { "CALCULATION", "AGGREGATE" },
      { "STATISTICS", "AGGREGATE" },
      { "REPORT", "MULTI_AGGREGATE" },
      { "ANALYSIS", "MULTI_AGGREGATE" },
      
      // Prediction/Forecasting
      { "PREDICTION", "MULTI_AGGREGATE" },
      { "FORECAST", "TREND" },
      { "FORECASTING", "TREND" },
      
      // Business metrics
      { "CUSTOMER_LIFETIME_VALUE", "MULTI_AGGREGATE" },
      { "CLV", "MULTI_AGGREGATE" },
      { "LIFETIME_VALUE", "MULTI_AGGREGATE" },
      { "REVENUE_ANALYSIS", "MULTI_AGGREGATE" },
      { "PROFITABILITY", "MULTI_AGGREGATE" },
      
      // Time-based
      { "TIME_SERIES", "TREND" },
      { "TEMPORAL", "TREND" },
      
      // Grouping
      { "GROUPING", "GROUP_BY" },
      { "CATEGORIZATION", "GROUP_BY" },
      
      // Ranking
      { "TOP", "TOP_N" },
      { "BOTTOM", "TOP_N" },
      
      // Comparison
      { "COMPARE", "COMPARISON" },
      { "VERSUS", "COMPARISON" },
      
      // Unknown/Unclear
      { "UNCLEAR", "Unknown" },
      { "AMBIGUOUS", "Unknown" }
    };

    if (mappings.TryGetValue(rawIntent, out var normalized))
    {
      return normalized;
    }

    // If not in mapping, check if it's a valid enum value
    if (Enum.TryParse<QueryIntent>(rawIntent, ignoreCase: true, out _))
    {
      return rawIntent;
    }

    // Default fallback
    _logger.LogWarning("[IntentAnalysis] Unknown intent '{Intent}', defaulting to Unknown", rawIntent);
    return "Unknown";
  }

  private static string BuildUserPrompt(string userQuery, string schemaContext)
  {
    var prompt = $"User Query: {userQuery}";

    if (!string.IsNullOrEmpty(schemaContext))
    {
      prompt += $"\n\nAvailable Tables: {schemaContext}";
    }

    prompt += "\n\nAnalyze this query and return the structured intent as JSON:";

    return prompt;
  }
}