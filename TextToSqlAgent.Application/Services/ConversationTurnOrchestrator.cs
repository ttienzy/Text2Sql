using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Helpers;
using TextToSqlAgent.Core.Models;
using Message = TextToSqlAgent.Infrastructure.Entities.Message;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Coordinates a single conversational turn while keeping persistence concerns
/// outside of the application layer.
/// </summary>
public class ConversationTurnOrchestrator
{
    private readonly EnhancedAgentOrchestrator _agent;
    private readonly ILogger<ConversationTurnOrchestrator> _logger;

    public ConversationTurnOrchestrator(
        EnhancedAgentOrchestrator agent,
        ILogger<ConversationTurnOrchestrator> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    public async Task<ConversationTurnResult> ExecuteAsync(
        ConversationTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();

        _logger.LogInformation(
            "[ConversationTurn] Processing turn for ConversationId={ConversationId}, ConnectionId={ConnectionId}",
            conversationId,
            request.ConnectionId);

        var response = await _agent.ProcessMessageWithIntentRoutingAsync(
            request.UserQuestion,
            request.ConnectionId,
            conversationId,
            request.ConversationHistory,
            request.PersistedContext,
            request.Progress,
            request.SqlTokenCallback,
            cancellationToken);

        var updatedContext = BuildUpdatedContext(
            request.PersistedContext,
            request.UserQuestion,
            response);

        return new ConversationTurnResult
        {
            ConversationId = conversationId,
            Response = response,
            UpdatedContext = updatedContext
        };
    }

    private static SerializableConversationContext BuildUpdatedContext(
        SerializableConversationContext? persistedContext,
        string userQuestion,
        UnifiedPipelineResponse response)
    {
        var context = CloneContext(persistedContext);

        foreach (var entity in response.Intent?.DetectedEntities ?? Enumerable.Empty<string>())
        {
            context.AddMentionedTable(entity);
        }

        if (response.Data is QueryPipelineData queryData)
        {
            foreach (var entity in queryData.ContextEntities)
            {
                context.AddMentionedTable(entity);
            }

            if (!string.IsNullOrWhiteSpace(queryData.PrimaryEntity))
            {
                context.AddMentionedTable(queryData.PrimaryEntity);
            }
        }

        if (!string.IsNullOrWhiteSpace(response.SqlGenerated))
        {
            var (tables, _, _, _) = SqlContextExtractor.ExtractFullContext(response.SqlGenerated);
            foreach (var table in tables)
            {
                context.AddMentionedTable(table);
            }

            context.UpdateLastSql(
                response.SqlGenerated,
                CreateResultSummary(userQuestion, response));
        }
        else if (!string.IsNullOrWhiteSpace(response.Message))
        {
            context.LastResultSummary = response.Message.Length > 240
                ? response.Message[..240]
                : response.Message;
            context.LastActiveAt = DateTime.UtcNow;
        }

        return context;
    }

    private static SerializableConversationContext CloneContext(SerializableConversationContext? source)
    {
        if (source == null)
        {
            return new SerializableConversationContext();
        }

        return new SerializableConversationContext
        {
            MentionedTables = source.MentionedTables.ToList(),
            Aliases = new Dictionary<string, string>(source.Aliases),
            LastSql = source.LastSql,
            LastResultSummary = source.LastResultSummary,
            UserPreferences = new Dictionary<string, string>(source.UserPreferences),
            LastActiveAt = source.LastActiveAt
        };
    }

    private static string CreateResultSummary(string userQuestion, UnifiedPipelineResponse response)
    {
        var summarySource = response.Message;

        if (response.Data is QueryPipelineData queryData &&
            queryData.QueryResult?.RowCount is int rowCount)
        {
            summarySource = $"{summarySource} Returned {rowCount} row(s).";
        }

        if (string.IsNullOrWhiteSpace(summarySource))
        {
            summarySource = userQuestion;
        }

        return summarySource.Length > 240
            ? summarySource[..240]
            : summarySource;
    }
}

public class ConversationTurnRequest
{
    public string UserQuestion { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public List<Message>? ConversationHistory { get; set; }
    public SerializableConversationContext? PersistedContext { get; set; }
    public IProgress<AgentStageEvent>? Progress { get; set; }
    public Action<string>? SqlTokenCallback { get; set; }
}

public class ConversationTurnResult
{
    public string ConversationId { get; set; } = string.Empty;
    public UnifiedPipelineResponse Response { get; set; } = null!;
    public SerializableConversationContext UpdatedContext { get; set; } = new();
}
