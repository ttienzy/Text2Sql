using Microsoft.Extensions.Logging;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Pipeline.Stages;

/// <summary>
/// Stage 2: Validation — validates and normalizes the user question,
/// enriches with conversation context, and checks database relevance.
/// </summary>
public class ValidationStage : IPipelineStage
{
    private readonly IAgentServiceFactory _serviceFactory;
    private readonly ILogger<ValidationStage> _logger;

    public string StageName => "Validation";
    public AgentStage Stage => AgentStage.VALIDATING;
    public double ProgressStart => 0.10;

    public ValidationStage(
        IAgentServiceFactory serviceFactory,
        ILogger<ValidationStage> logger)
    {
        _serviceFactory = serviceFactory;
        _logger = logger;
    }

    public async Task<StageResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        context.Steps.Add("Validate query relevance");

        // ── Step 1: Enrich question with conversation context ──
        var conversationManager = _serviceFactory.GetConversationManager();
        var conversationCtx = conversationManager.GetOrCreateContext(context.ConversationId);

        // Populate context from conversation history if available
        if (context.ConversationHistory?.Any() == true)
        {
            PopulateConversationContext(conversationCtx, context.ConversationHistory);
        }

        context.ConversationCtx = conversationCtx;

        // Pronoun detection + question enrichment
        var pronounsDetected = false;
        if (conversationCtx.History.Count > 0)
        {
            var resolver = _serviceFactory.GetOrCreate<CoreferenceResolver>();
            pronounsDetected = resolver.ContainsPronouns(context.UserQuestion);
        }

        var enrichedQuestion = conversationManager.EnrichQuestionWithContext(conversationCtx, context.UserQuestion);
        context.EnrichedQuestion = enrichedQuestion;

        if (enrichedQuestion != context.UserQuestion)
        {
            _logger.LogInformation(
                "[Validation] Question enriched:\n  Original: '{Original}'\n  Enriched: '{Enriched}'",
                context.UserQuestion, enrichedQuestion);

            // Store context entities for response
            if (conversationCtx.History.Any())
            {
                var lastTurn = conversationCtx.History.Last();
                if (lastTurn.EntitiesReferenced.Any())
                {
                    context.Response.ContextEntities = lastTurn.EntitiesReferenced;
                    context.Response.PrimaryEntity = lastTurn.PrimaryEntity;
                    context.Response.PronounsResolved = pronounsDetected;
                }
            }
        }

        // ── Step 2: Validate query relevance ──
        var queryValidator = _serviceFactory.GetQueryValidator();
        var validation = await queryValidator.ValidateQueryAsync(
            enrichedQuestion,
            new List<string>(), // Empty — validator uses heuristics
            ct);

        context.ValidationResult = validation;

        _logger.LogInformation(
            "[Validation] Type: {Type}, Relevant: {Relevant}, Confidence: {Confidence:P0}",
            validation.QueryType, validation.IsRelevant, validation.Confidence);

        // Handle non-database queries (fast path)
        if (!validation.IsRelevant)
        {
            context.Response.Success = true;
            context.Response.Answer = validation.SuggestedResponse ??
                "I'm a database assistant. Please ask a database-related question.";

            conversationManager.AddTurn(conversationCtx, context.UserQuestion,
                context.Response.Answer, success: true);

            return StageResult.Stop("Non-database query rejected");
        }

        // Handle clarification needed
        if (validation.NeedsClarification)
        {
            var clarification = validation.ClarificationQuestion ?? "Please clarify your question.";
            context.Response.Success = false;
            context.Response.Answer = clarification;
            context.Response.ErrorMessage = clarification;

            conversationManager.AddTurn(conversationCtx, context.UserQuestion,
                context.Response.Answer, success: false);

            return StageResult.Stop("Clarification needed");
        }

        // ── Step 3: Normalize prompt ──
        context.Steps.Add("Normalize with conversation context");
        var normalizeTask = _serviceFactory.GetOrCreate<TextToSqlAgent.Core.Tasks.NormalizePromptTask>();
        context.NormalizedPrompt = await normalizeTask.ExecuteAsync(enrichedQuestion, ct);

        return StageResult.Continue();
    }

    private void PopulateConversationContext(
        ConversationContext ctx,
        List<TextToSqlAgent.Infrastructure.Entities.Message> history)
    {
        _logger.LogInformation("[Validation] Populating context with {Count} messages from DB", history.Count);

        var turns = new List<ConversationTurn>();
        TextToSqlAgent.Infrastructure.Entities.Message? lastUserMessage = null;

        foreach (var msg in history.OrderBy(m => m.CreatedAt))
        {
            if (msg.Role == "user")
            {
                lastUserMessage = msg;
            }
            else if (msg.Role == "assistant" && lastUserMessage != null)
            {
                string? targetTable = null;
                List<string> entitiesReferenced = new();
                string? primaryEntity = null;
                Dictionary<string, string> columns = new();
                string? queryIntentType = null;

                if (!string.IsNullOrEmpty(msg.SqlQuery))
                {
                    var (tables, primary, cols, intentType) =
                        TextToSqlAgent.Core.Helpers.SqlContextExtractor.ExtractFullContext(msg.SqlQuery);
                    entitiesReferenced = tables;
                    primaryEntity = primary;
                    targetTable = primary;
                    columns = cols;
                    queryIntentType = intentType;
                }

                turns.Add(new ConversationTurn
                {
                    TurnNumber = turns.Count + 1,
                    UserQuestion = lastUserMessage.Content ?? string.Empty,
                    SystemResponse = msg.Content ?? string.Empty,
                    SqlQuery = msg.SqlQuery,
                    TargetTable = targetTable,
                    EntitiesReferenced = entitiesReferenced,
                    PrimaryEntity = primaryEntity,
                    Columns = columns,
                    QueryIntentType = queryIntentType,
                    Timestamp = msg.CreatedAt,
                    Success = msg.Success
                });
                lastUserMessage = null;
            }
        }

        ctx.History = turns;
        _logger.LogInformation("[Validation] Context populated with {Count} turns", turns.Count);
    }
}
