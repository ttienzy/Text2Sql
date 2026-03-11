using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Analysis;

/// <summary>
/// Detects ambiguities in natural language questions
/// </summary>
public class AmbiguityDetector
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<AmbiguityDetector> _logger;

    public AmbiguityDetector(
        ILLMClient llmClient,
        ILogger<AmbiguityDetector> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    /// <summary>
    /// Detect ambiguities in question with schema context
    /// </summary>
    public async Task<AmbiguityAnalysis> DetectAsync(
        string question,
        RetrievedSchemaContext schemaContext,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Detecting ambiguities in question: {Question}", question);

        var ambiguities = new List<Ambiguity>();

        // Rule-based detection first (fast)
        ambiguities.AddRange(DetectRuleBasedAmbiguities(question, schemaContext));

        // LLM-based detection for complex cases
        if (ambiguities.Count == 0 || ShouldUseLLMDetection(question))
        {
            try
            {
                var llmAmbiguities = await DetectWithLLMAsync(question, schemaContext, ct);
                ambiguities.AddRange(llmAmbiguities);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM ambiguity detection failed");
            }
        }

        return new AmbiguityAnalysis
        {
            Question = question,
            HasAmbiguity = ambiguities.Count > 0,
            Ambiguities = ambiguities,
            Confidence = CalculateConfidence(ambiguities)
        };
    }

    /// <summary>
    /// Rule-based ambiguity detection
    /// </summary>
    private List<Ambiguity> DetectRuleBasedAmbiguities(
        string question,
        RetrievedSchemaContext schemaContext)
    {
        var ambiguities = new List<Ambiguity>();
        var lowerQuestion = question.ToLower();

        // 1. Multiple columns with similar names
        var allColumns = schemaContext.TableColumns
            .SelectMany(kvp => kvp.Value.Select(c => new { Table = kvp.Key, Column = c }))
            .ToList();

        var columnGroups = allColumns
            .GroupBy(c => NormalizeColumnName(c.Column.ColumnName))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in columnGroups)
        {
            var columns = group.ToList();
            if (columns.Count > 1)
            {
                ambiguities.Add(new Ambiguity
                {
                    Type = AmbiguityType.MultipleColumns,
                    Message = $"Multiple columns match '{group.Key}': {string.Join(", ", columns.Select(c => $"{c.Table}.{c.Column.ColumnName}"))}",
                    Options = columns.Select(c => $"{c.Table}.{c.Column.ColumnName}").ToList(),
                    Severity = AmbiguitySeverity.High
                });
            }
        }

        // 2. Missing time range for time-based queries
        if (ContainsTimeKeywords(lowerQuestion) && !ContainsTimeRange(lowerQuestion))
        {
            ambiguities.Add(new Ambiguity
            {
                Type = AmbiguityType.MissingTimeRange,
                Message = "Question mentions time but no specific time range provided",
                Options = new List<string> { "today", "this week", "this month", "this year", "last 30 days" },
                Severity = AmbiguitySeverity.Medium
            });
        }

        // 3. Unclear aggregation
        if (ContainsAggregationKeywords(lowerQuestion) && !ContainsExplicitAggregation(lowerQuestion))
        {
            ambiguities.Add(new Ambiguity
            {
                Type = AmbiguityType.UnclearAggregation,
                Message = "Aggregation implied but not explicit",
                Options = new List<string> { "SUM", "AVG", "COUNT", "MAX", "MIN" },
                Severity = AmbiguitySeverity.Medium
            });
        }

        // 4. Ambiguous entity references
        var entities = ExtractPotentialEntities(question);
        foreach (var entity in entities)
        {
            var matchingTables = schemaContext.RelevantTables
                .Where(t => t.TableName.Contains(entity, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingTables.Count > 1)
            {
                ambiguities.Add(new Ambiguity
                {
                    Type = AmbiguityType.AmbiguousEntity,
                    Message = $"Entity '{entity}' could refer to multiple tables",
                    Options = matchingTables.Select(t => t.TableName).ToList(),
                    Severity = AmbiguitySeverity.High
                });
            }
        }

        // 5. Missing comparison operator
        if (ContainsComparisonKeywords(lowerQuestion) && !ContainsExplicitOperator(lowerQuestion))
        {
            ambiguities.Add(new Ambiguity
            {
                Type = AmbiguityType.MissingOperator,
                Message = "Comparison implied but operator not clear",
                Options = new List<string> { "greater than", "less than", "equal to", "between" },
                Severity = AmbiguitySeverity.Low
            });
        }

        return ambiguities;
    }

    /// <summary>
    /// LLM-based ambiguity detection
    /// </summary>
    private async Task<List<Ambiguity>> DetectWithLLMAsync(
        string question,
        RetrievedSchemaContext schemaContext,
        CancellationToken ct)
    {
        var schemaInfo = FormatSchemaInfo(schemaContext);

        var prompt = $@"Analyze this question for ambiguities given the database schema.

Question: {question}

Available Schema:
{schemaInfo}

Identify ambiguities such as:
1. Multiple possible interpretations
2. Missing information (time ranges, filters, etc.)
3. Unclear references to tables/columns
4. Ambiguous aggregations or groupings

Return JSON array:
[
  {{
    ""type"": ""MultipleColumns|MissingTimeRange|UnclearAggregation|AmbiguousEntity|Other"",
    ""message"": ""description of ambiguity"",
    ""options"": [""option1"", ""option2""],
    ""severity"": ""High|Medium|Low""
  }}
]

If no ambiguities found, return empty array [].";

        var response = await _llmClient.CompleteAsync(prompt, ct);
        return ParseAmbiguities(response);
    }

    /// <summary>
    /// Format schema info for LLM
    /// </summary>
    private string FormatSchemaInfo(RetrievedSchemaContext schemaContext)
    {
        var info = "";
        foreach (var table in schemaContext.RelevantTables.Take(5))
        {
            info += $"Table: {table.TableName}\n";
            if (schemaContext.TableColumns.TryGetValue(table.TableName, out var columns))
            {
                info += $"  Columns: {string.Join(", ", columns.Select(c => c.ColumnName))}\n";
            }
        }
        return info;
    }

    /// <summary>
    /// Parse ambiguities from LLM response
    /// </summary>
    private List<Ambiguity> ParseAmbiguities(string response)
    {
        try
        {
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var ambiguities = System.Text.Json.JsonSerializer.Deserialize<List<Ambiguity>>(
                    json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return ambiguities ?? new List<Ambiguity>();
            }

            return new List<Ambiguity>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse ambiguities from LLM response");
            return new List<Ambiguity>();
        }
    }

    // Helper methods
    private string NormalizeColumnName(string name) =>
        name.ToLower().Replace("_", "").Replace(" ", "");

    private bool ContainsTimeKeywords(string text) =>
        new[] { "today", "yesterday", "week", "month", "year", "date", "time", "recent", "last", "this" }
            .Any(k => text.Contains(k));

    private bool ContainsTimeRange(string text) =>
        new[] { "between", "from", "to", "since", "until", "in 2023", "in 2024" }
            .Any(k => text.Contains(k));

    private bool ContainsAggregationKeywords(string text) =>
        new[] { "total", "average", "sum", "count", "how many", "number of" }
            .Any(k => text.Contains(k));

    private bool ContainsExplicitAggregation(string text) =>
        new[] { "sum of", "average of", "count of", "total of" }
            .Any(k => text.Contains(k));

    private bool ContainsComparisonKeywords(string text) =>
        new[] { "more than", "less than", "higher", "lower", "above", "below", "top", "bottom" }
            .Any(k => text.Contains(k));

    private bool ContainsExplicitOperator(string text) =>
        new[] { ">", "<", ">=", "<=", "=", "greater than", "less than", "equal to" }
            .Any(k => text.Contains(k));

    private List<string> ExtractPotentialEntities(string question)
    {
        // Simple entity extraction - can be improved with NER
        var words = question.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Where(w => w.Length > 3 && char.IsUpper(w[0])).ToList();
    }

    private bool ShouldUseLLMDetection(string question) =>
        question.Split(' ').Length > 10; // Use LLM for complex questions

    private double CalculateConfidence(List<Ambiguity> ambiguities)
    {
        if (ambiguities.Count == 0)
            return 1.0;

        var highCount = ambiguities.Count(a => a.Severity == AmbiguitySeverity.High);
        var mediumCount = ambiguities.Count(a => a.Severity == AmbiguitySeverity.Medium);

        return 1.0 - (highCount * 0.3 + mediumCount * 0.15);
    }
}

/// <summary>
/// Analysis result containing detected ambiguities
/// </summary>
public class AmbiguityAnalysis
{
    public string Question { get; set; } = string.Empty;
    public bool HasAmbiguity { get; set; }
    public List<Ambiguity> Ambiguities { get; set; } = new();
    public double Confidence { get; set; }

    public string GetClarificationPrompt()
    {
        if (!HasAmbiguity)
            return string.Empty;

        var prompt = "I need some clarification:\n\n";
        for (int i = 0; i < Ambiguities.Count; i++)
        {
            var amb = Ambiguities[i];
            prompt += $"{i + 1}. {amb.Message}\n";
            if (amb.Options.Count > 0)
            {
                prompt += $"   Options: {string.Join(", ", amb.Options)}\n";
            }
            prompt += "\n";
        }

        return prompt;
    }
}

/// <summary>
/// Represents a single ambiguity
/// </summary>
public class Ambiguity
{
    public AmbiguityType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public AmbiguitySeverity Severity { get; set; }
}

public enum AmbiguityType
{
    MultipleColumns,
    MissingTimeRange,
    UnclearAggregation,
    AmbiguousEntity,
    MissingOperator,
    Other
}

public enum AmbiguitySeverity
{
    Low,
    Medium,
    High
}
