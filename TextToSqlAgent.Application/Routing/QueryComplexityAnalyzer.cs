using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Entities;
using DatabaseSchema = TextToSqlAgent.Core.Models.DatabaseSchema;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Semantic query complexity analyzer using NLP techniques
/// Detects implicit JOINs, temporal patterns, aggregation complexity
/// </summary>
public class QueryComplexityAnalyzer
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<QueryComplexityAnalyzer> _logger;

    // Temporal keywords indicating time-based queries
    private static readonly string[] TemporalKeywords = new[]
    {
        "last month", "this year", "yesterday", "today", "trend", "over time",
        "tháng trước", "năm nay", "hôm qua", "hôm nay", "xu hướng", "theo thời gian",
        "quarter", "q1", "q2", "q3", "q4", "quý"
    };

    // Comparison keywords indicating complex analysis
    private static readonly string[] ComparisonKeywords = new[]
    {
        "compare", "versus", "vs", "difference", "between",
        "so sánh", "khác biệt", "giữa", "với"
    };

    // Aggregation keywords
    private static readonly string[] AggregationKeywords = new[]
    {
        "total", "sum", "average", "count", "max", "min", "group by",
        "tổng", "trung bình", "đếm", "nhiều nhất", "ít nhất", "nhóm"
    };

    public QueryComplexityAnalyzer(
        ILLMClient llmClient,
        ILogger<QueryComplexityAnalyzer> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<ComplexityScore> AnalyzeAsync(
        string query,
        DatabaseSchema schema,
        List<Message>? conversationHistory = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[ComplexityAnalyzer] Analyzing query: {Query}", query);

        // Step 1: Rule-based quick analysis
        var quickScore = AnalyzeWithRules(query, schema);

        // Step 2: If ambiguous, use LLM for deeper analysis
        if (quickScore.Confidence < 0.75)
        {
            _logger.LogDebug("[ComplexityAnalyzer] Low confidence ({Conf}), using LLM analysis", quickScore.Confidence);
            var llmScore = await AnalyzeWithLlmAsync(query, schema, conversationHistory, ct);

            // Combine scores (weighted average)
            return CombineScores(quickScore, llmScore);
        }

        return quickScore;
    }

    private ComplexityScore AnalyzeWithRules(string query, DatabaseSchema schema)
    {
        var lowerQuery = query.ToLowerInvariant();
        var signals = new List<(string Signal, double Weight, ComplexityLevel Level)>();

        // Signal 1: Detect implicit JOINs
        var implicitJoins = DetectImplicitJoins(lowerQuery, schema);
        if (implicitJoins.Count > 0)
        {
            signals.Add(("Implicit JOINs detected", 0.8, ComplexityLevel.Medium));
            _logger.LogDebug("[ComplexityAnalyzer] Detected {Count} implicit JOINs: {Joins}",
                implicitJoins.Count, string.Join(", ", implicitJoins));
        }

        // Signal 2: Temporal patterns
        if (TemporalKeywords.Any(k => lowerQuery.Contains(k)))
        {
            signals.Add(("Temporal pattern", 0.7, ComplexityLevel.Medium));
        }

        // Signal 3: Comparison/analysis
        if (ComparisonKeywords.Any(k => lowerQuery.Contains(k)))
        {
            signals.Add(("Comparison analysis", 0.9, ComplexityLevel.Complex));
        }

        // Signal 4: Aggregation
        var aggCount = AggregationKeywords.Count(k => lowerQuery.Contains(k));
        if (aggCount > 0)
        {
            var level = aggCount > 2 ? ComplexityLevel.Complex : ComplexityLevel.Medium;
            signals.Add(($"{aggCount} aggregations", 0.6, level));
        }

        // Signal 5: Multiple entities mentioned
        var mentionedTables = schema.Tables
            .Where(t => lowerQuery.Contains(t.TableName.ToLowerInvariant()))
            .ToList();

        if (mentionedTables.Count > 2)
        {
            signals.Add(($"{mentionedTables.Count} tables mentioned", 0.8, ComplexityLevel.Complex));
        }
        else if (mentionedTables.Count == 2)
        {
            signals.Add(("2 tables mentioned", 0.7, ComplexityLevel.Medium));
        }

        // Calculate overall complexity
        if (signals.Count == 0)
        {
            return new ComplexityScore
            {
                Level = ComplexityLevel.Simple,
                Confidence = 0.8,
                Reasoning = "No complex patterns detected",
                RequiredTables = mentionedTables.Select(t => t.TableName).ToList(),
                EstimatedLlmCalls = 2
            };
        }

        // Weighted voting
        var complexVotes = signals.Count(s => s.Level == ComplexityLevel.Complex) * 2.0;
        var mediumVotes = signals.Count(s => s.Level == ComplexityLevel.Medium) * 1.0;
        var totalVotes = complexVotes + mediumVotes;

        ComplexityLevel finalLevel;
        if (complexVotes > mediumVotes)
            finalLevel = ComplexityLevel.Complex;
        else if (mediumVotes > 0)
            finalLevel = ComplexityLevel.Medium;
        else
            finalLevel = ComplexityLevel.Simple;

        var confidence = Math.Min(signals.Average(s => s.Weight), 0.95);

        return new ComplexityScore
        {
            Level = finalLevel,
            Confidence = confidence,
            Reasoning = $"Detected: {string.Join(", ", signals.Select(s => s.Signal))}",
            RequiredTables = mentionedTables.Select(t => t.TableName).ToList(),
            EstimatedLlmCalls = finalLevel switch
            {
                ComplexityLevel.Simple => 2,
                ComplexityLevel.Medium => 4,
                ComplexityLevel.Complex => 6,
                _ => 3
            }
        };
    }

    private List<string> DetectImplicitJoins(string lowerQuery, DatabaseSchema schema)
    {
        var implicitJoins = new List<string>();

        // Pattern 1: "customers who bought products" → Customers JOIN Orders JOIN Products
        var patterns = new[]
        {
            (@"(\w+)\s+who\s+(\w+)\s+(\w+)", "English: entity who action entity"),
            (@"(\w+)\s+mà\s+(\w+)\s+(\w+)", "Vietnamese: entity mà action entity"),
            (@"(\w+)\s+của\s+(\w+)", "Vietnamese: entity của entity"),
            (@"(\w+)'s\s+(\w+)", "English: entity's entity")
        };

        foreach (var (pattern, description) in patterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            if (regex.IsMatch(lowerQuery))
            {
                implicitJoins.Add(description);
            }
        }

        return implicitJoins;
    }

    private async Task<ComplexityScore> AnalyzeWithLlmAsync(
        string query,
        DatabaseSchema schema,
        List<Message>? conversationHistory,
        CancellationToken ct)
    {
        var historyContext = conversationHistory != null && conversationHistory.Any()
            ? $"\n\nConversation History:\n{string.Join("\n", conversationHistory.TakeLast(3).Select(m => $"- {m.Role}: {m.Content}"))}"
            : "";

        var prompt = $@"Analyze this database query complexity:

Query: ""{query}""

Database Schema:
{FormatSchemaForPrompt(schema)}
{historyContext}

Analyze:
1. What tables are needed?
2. Are JOINs required (explicit or implicit)?
3. What aggregations are needed?
4. Is temporal analysis required?
5. Is comparison/trend analysis needed?

Rate complexity (0.0-1.0):
- 0.0-0.3: Simple (single table, basic SELECT)
- 0.4-0.6: Medium (JOINs, aggregations, time filters)
- 0.7-1.0: Complex (subqueries, comparisons, multi-step analysis)

Return JSON:
{{
  ""complexity_score"": 0.0-1.0,
  ""required_tables"": [""table1"", ""table2""],
  ""needs_joins"": true/false,
  ""needs_aggregation"": true/false,
  ""needs_temporal_analysis"": true/false,
  ""reasoning"": ""explanation""
}}";

        var systemPrompt = "You are a database query complexity analyzer. Return ONLY valid JSON.";
        var response = await _llmClient.CompleteWithSystemPromptAsync(systemPrompt, prompt, ct);

        return ParseLlmResponse(response, query);
    }

    private ComplexityScore ParseLlmResponse(string response, string originalQuery)
    {
        try
        {
            var cleaned = response.Trim()
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            var jsonStart = cleaned.IndexOf('{');
            var jsonEnd = cleaned.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                cleaned = cleaned[jsonStart..(jsonEnd + 1)];
            }

            var doc = System.Text.Json.JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var complexityScore = root.GetProperty("complexity_score").GetDouble();
            var requiredTables = root.TryGetProperty("required_tables", out var tables)
                ? tables.EnumerateArray().Select(t => t.GetString() ?? "").ToList()
                : new List<string>();
            var reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";

            ComplexityLevel level;
            if (complexityScore < 0.4)
                level = ComplexityLevel.Simple;
            else if (complexityScore < 0.7)
                level = ComplexityLevel.Medium;
            else
                level = ComplexityLevel.Complex;

            return new ComplexityScore
            {
                Level = level,
                Confidence = 0.85, // LLM analysis has high confidence
                Reasoning = reasoning,
                RequiredTables = requiredTables,
                EstimatedLlmCalls = level switch
                {
                    ComplexityLevel.Simple => 2,
                    ComplexityLevel.Medium => 4,
                    ComplexityLevel.Complex => 6,
                    _ => 3
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ComplexityAnalyzer] Failed to parse LLM response, using fallback");
            return new ComplexityScore
            {
                Level = ComplexityLevel.Medium,
                Confidence = 0.5,
                Reasoning = "LLM parse failed, defaulting to Medium",
                RequiredTables = new List<string>(),
                EstimatedLlmCalls = 4
            };
        }
    }

    private ComplexityScore CombineScores(ComplexityScore ruleScore, ComplexityScore llmScore)
    {
        // Weighted average: 40% rules, 60% LLM
        var combinedConfidence = (ruleScore.Confidence * 0.4) + (llmScore.Confidence * 0.6);

        // If both agree, use that level with high confidence
        if (ruleScore.Level == llmScore.Level)
        {
            return new ComplexityScore
            {
                Level = ruleScore.Level,
                Confidence = Math.Max(combinedConfidence, 0.85),
                Reasoning = $"Rules + LLM agree: {ruleScore.Reasoning}",
                RequiredTables = llmScore.RequiredTables,
                EstimatedLlmCalls = llmScore.EstimatedLlmCalls
            };
        }

        // If disagree, trust LLM more but lower confidence
        return new ComplexityScore
        {
            Level = llmScore.Level,
            Confidence = combinedConfidence * 0.8,
            Reasoning = $"Rules suggested {ruleScore.Level}, LLM suggested {llmScore.Level}. Using LLM.",
            RequiredTables = llmScore.RequiredTables,
            EstimatedLlmCalls = llmScore.EstimatedLlmCalls
        };
    }

    private string FormatSchemaForPrompt(DatabaseSchema schema)
    {
        // Remove 10-table limit - include all tables for better analysis
        var tables = schema.Tables.Select(t =>
            $"{t.TableName}({string.Join(", ", t.Columns.Select(c => c.ColumnName))})");
        return string.Join("\n", tables);
    }
}

/// <summary>
/// Complexity score result
/// </summary>
public class ComplexityScore
{
    public ComplexityLevel Level { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = "";
    public List<string> RequiredTables { get; set; } = new();
    public int EstimatedLlmCalls { get; set; }
}

/// <summary>
/// Query complexity levels
/// </summary>
public enum ComplexityLevel
{
    Simple,
    Medium,
    Complex
}
