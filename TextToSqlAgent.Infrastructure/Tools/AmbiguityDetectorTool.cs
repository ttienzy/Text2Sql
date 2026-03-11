using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.Tools;

/// <summary>
/// Tool for detecting ambiguities in natural language questions
/// </summary>
public class AmbiguityDetectorTool : ITool
{
    private readonly AmbiguityDetector _detector;
    private readonly ILogger<AmbiguityDetectorTool> _logger;

    public string Name => "detect_ambiguity";
    public string Description => "Detect ambiguities in the question that need clarification";

    public ToolSchema Schema => new()
    {
        Parameters = new List<ToolParameter>
        {
            new() {
                Name = "question",
                Type = "string",
                Description = "The natural language question to analyze",
                Required = true
            },
            new() {
                Name = "schema_context",
                Type = "object",
                Description = "The retrieved schema context",
                Required = true
            }
        }
    };

    public AmbiguityDetectorTool(
        AmbiguityDetector detector,
        ILogger<AmbiguityDetectorTool> logger)
    {
        _detector = detector;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken ct = default)
    {
        try
        {
            var question = input.GetString("question", "query");
            var schemaContext = input.Get<RetrievedSchemaContext>("schema_context");

            if (schemaContext == null)
            {
                return ToolResult.FromError("schema_context is required");
            }

            _logger.LogInformation("Detecting ambiguities in question: {Question}", question);

            var analysis = await _detector.DetectAsync(question, schemaContext, ct);

            if (analysis.HasAmbiguity)
            {
                _logger.LogWarning(
                    "Found {Count} ambiguities with confidence {Confidence:P0}",
                    analysis.Ambiguities.Count,
                    analysis.Confidence);
            }

            return ToolResult.FromSuccess(new
            {
                has_ambiguity = analysis.HasAmbiguity,
                confidence = analysis.Confidence,
                ambiguities = analysis.Ambiguities.Select(a => new
                {
                    type = a.Type.ToString(),
                    message = a.Message,
                    options = a.Options,
                    severity = a.Severity.ToString()
                }),
                clarification_prompt = analysis.GetClarificationPrompt()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ambiguity detection failed");
            return ToolResult.FromError($"Detection failed: {ex.Message}");
        }
    }
}
