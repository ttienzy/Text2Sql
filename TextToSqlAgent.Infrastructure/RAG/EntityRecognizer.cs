using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.RAG;

/// <summary>
/// Extracts entities (tables, columns, values) from natural language questions
/// </summary>
public class EntityRecognizer
{
    private readonly ILLMClient _llm;
    private readonly ILogger<EntityRecognizer> _logger;

    public EntityRecognizer(ILLMClient llm, ILogger<EntityRecognizer> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<EntityRecognitionResult> RecognizeAsync(
        string question,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[EntityRecognizer] Extracting entities from: {Question}", question);

        var prompt = BuildPrompt(question);
        var response = await _llm.CompleteAsync(prompt, ct);

        var result = ParseResponse(response);

        _logger.LogInformation("[EntityRecognizer] Found {Count} entities: {Tables} tables, {Columns} columns",
            result.AllEntities.Count, result.Tables.Count, result.Columns.Count);

        return result;
    }

    private string BuildPrompt(string question)
    {
        return $@"Extract database entities from this question.

Question: ""{question}""

Identify:
1. Table names (e.g., ""customers"", ""orders"", ""products"")
2. Column names (e.g., ""name"", ""price"", ""date"")
3. Values (e.g., ""New York"", ""2024"", ""Electronics"")
4. Operators (e.g., ""greater than"", ""equals"", ""between"")
5. Aggregations (e.g., ""count"", ""sum"", ""average"", ""top"")
6. Time ranges (e.g., ""this month"", ""last year"", ""today"")

Return ONLY valid JSON:
{{
  ""tables"": [""table1"", ""table2""],
  ""columns"": [""column1"", ""column2""],
  ""values"": [""value1"", ""value2""],
  ""operators"": [""operator1""],
  ""aggregations"": [""aggregation1""],
  ""time_ranges"": [""time_range1""]
}}

Rules:
- Use lowercase
- Use singular form for tables
- Extract implicit entities (e.g., ""revenue"" implies a column)
- Return empty arrays if none found";
    }

    private EntityRecognitionResult ParseResponse(string response)
    {
        try
        {
            // Clean response
            var json = response.Replace("```json", "").Replace("```", "").Trim();

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new EntityRecognitionResult();

            // Parse tables
            if (root.TryGetProperty("tables", out var tables))
            {
                foreach (var item in tables.EnumerateArray())
                {
                    result.Tables.Add(new Entity
                    {
                        Text = item.GetString() ?? "",
                        Type = EntityType.Table,
                        Confidence = 0.8
                    });
                }
            }

            // Parse columns
            if (root.TryGetProperty("columns", out var columns))
            {
                foreach (var item in columns.EnumerateArray())
                {
                    result.Columns.Add(new Entity
                    {
                        Text = item.GetString() ?? "",
                        Type = EntityType.Column,
                        Confidence = 0.7
                    });
                }
            }

            // Parse values
            if (root.TryGetProperty("values", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    result.Values.Add(new Entity
                    {
                        Text = item.GetString() ?? "",
                        Type = EntityType.Value,
                        Confidence = 0.9
                    });
                }
            }

            // Parse operators
            if (root.TryGetProperty("operators", out var operators))
            {
                foreach (var item in operators.EnumerateArray())
                {
                    result.Operators.Add(new Entity
                    {
                        Text = item.GetString() ?? "",
                        Type = EntityType.Operator,
                        Confidence = 0.8
                    });
                }
            }

            // Parse aggregations
            if (root.TryGetProperty("aggregations", out var aggregations))
            {
                foreach (var item in aggregations.EnumerateArray())
                {
                    result.Aggregations.Add(new Entity
                    {
                        Text = item.GetString() ?? "",
                        Type = EntityType.Aggregation,
                        Confidence = 0.9
                    });
                }
            }

            // Parse time ranges
            if (root.TryGetProperty("time_ranges", out var timeRanges))
            {
                foreach (var item in timeRanges.EnumerateArray())
                {
                    result.TimeRanges.Add(new Entity
                    {
                        Text = item.GetString() ?? "",
                        Type = EntityType.TimeRange,
                        Confidence = 0.8
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EntityRecognizer] Failed to parse response: {Response}", response);
            return new EntityRecognitionResult();
        }
    }
}
