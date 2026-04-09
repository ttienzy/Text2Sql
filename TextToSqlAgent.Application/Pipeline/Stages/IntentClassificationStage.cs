using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage 1: Intent Classification — routes queries to the appropriate pipeline
/// (QUERY, WRITE, DDL, FORBIDDEN, REJECT) before any heavy processing.
/// </summary>
public class IntentClassificationStage : IPipelineStage
{
    private readonly IIntentClassifier? _intentClassifier;
    private readonly IForbiddenPipeline? _forbiddenPipeline;
    private readonly ILogger<IntentClassificationStage> _logger;

    public string StageName => "Intent Classification";
    public AgentStage Stage => AgentStage.CLASSIFYING;
    public double ProgressStart => 0.05;

    public IntentClassificationStage(
        ILogger<IntentClassificationStage> logger,
        IIntentClassifier? intentClassifier = null,
        IForbiddenPipeline? forbiddenPipeline = null)
    {
        _logger = logger;
        _intentClassifier = intentClassifier;
        _forbiddenPipeline = forbiddenPipeline;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        if (_intentClassifier == null)
        {
            _logger.LogDebug("[IntentClassification] No intent classifier available, skipping");
            return StageResult.Continue();
        }

        context.Steps.Add("Intent classification and routing");

        var conversationContext = BuildConversationContext(context.ConversationHistory);

        // Build database context from cached schema if available
        var databaseContext = context.Schema != null
            ? string.Join(", ", context.Schema.Tables.Take(20).Select(t => t.TableName))
            : "";

        var intentResult = await _intentClassifier.ClassifyAsync(
            context.EnrichedQuestion,
            conversationContext,
            databaseContext,
            ct);

        context.IntentClassification = intentResult;

        _logger.LogInformation(
            "[IntentClassification] Intent: {Intent} → Route: {Route} (confidence: {Confidence:P0})",
            intentResult.Intent, intentResult.Route, intentResult.Confidence);

        // Route to specialized pipelines
        switch (intentResult.Route)
        {
            case PipelineRoute.Write:
                _logger.LogInformation("[IntentClassification] → WRITE operation detected");
                context.Response.Success = true;
                context.Response.Answer = "⚠️ This is a write operation (INSERT/UPDATE). Please use the Write pipeline endpoint.";
                context.Response.Metadata = new Dictionary<string, object>
                {
                    ["pipeline"] = "WRITE",
                    ["intent"] = intentResult.Intent.ToString(),
                    ["isWriteOperation"] = true,
                    ["requiresConfirmation"] = true,
                    ["detectedEntities"] = intentResult.DetectedEntities,
                    ["suggestedEndpoint"] = "/api/write/preview"
                };
                return StageResult.Stop("Routed to WRITE pipeline");

            case PipelineRoute.Ddl:
                _logger.LogInformation("[IntentClassification] → DDL operation detected");
                context.Response.Success = true;
                context.Response.Answer = "⚠️ This is a DDL operation (CREATE/ALTER/DROP). Please use the DDL pipeline endpoint.";
                context.Response.Metadata = new Dictionary<string, object>
                {
                    ["pipeline"] = "DDL",
                    ["intent"] = intentResult.Intent.ToString(),
                    ["isDdlOperation"] = true,
                    ["requiresConfirmation"] = true,
                    ["detectedEntities"] = intentResult.DetectedEntities,
                    ["suggestedEndpoint"] = "/api/ddl/preview"
                };
                return StageResult.Stop("Routed to DDL pipeline");

            case PipelineRoute.Forbidden:
                _logger.LogWarning("[IntentClassification] → FORBIDDEN operation detected: {Reason}", intentResult.ForbiddenReason);
                if (_forbiddenPipeline != null)
                {
                    var forbiddenResult = await _forbiddenPipeline.RejectAsync(context.UserQuestion, intentResult, ct);
                    context.Response.Success = true;
                    context.Response.Answer = forbiddenResult.UserFacingMessage;
                    context.Response.Metadata = new Dictionary<string, object>
                    {
                        ["isForbidden"] = true,
                        ["forbiddenReason"] = forbiddenResult.RejectionReason,
                        ["safeAlternatives"] = forbiddenResult.SafeAlternatives,
                        ["detectedPatterns"] = forbiddenResult.DetectedPatterns ?? new List<string>()
                    };
                    return StageResult.Stop("Forbidden operation rejected");
                }
                break;

            case PipelineRoute.Reject:
                _logger.LogInformation("[IntentClassification] → Query rejected: {Reason}", intentResult.Reasoning);
                var rejectMessage = intentResult.Intent switch
                {
                    IntentCategory.OffTopic => "I'm a database assistant. I can only help with database-related questions.",
                    IntentCategory.Unknown => "I couldn't understand your request. Please be more specific.",
                    _ => "I cannot process this request."
                };
                context.Response.Success = false;
                context.Response.Answer = rejectMessage;
                context.Response.ErrorMessage = rejectMessage;
                return StageResult.Stop("Query rejected");

            case PipelineRoute.Query:
            default:
                _logger.LogInformation("[IntentClassification] → Routing to QUERY pipeline");
                break;
        }

        return StageResult.Continue();
    }

    private static string BuildConversationContext(List<TextToSqlAgent.Infrastructure.Entities.Message>? history)
    {
        if (history == null || !history.Any()) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Recent conversation:");
        foreach (var msg in history.TakeLast(5))
        {
            sb.AppendLine($"{msg.Role}: {msg.Content}");
        }
        return sb.ToString();
    }
}
