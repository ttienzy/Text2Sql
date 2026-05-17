using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.Infrastructure.Services;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// [DEPRECATED] Enhanced Agent Controller with conversation awareness.
/// Use StreamingAgentController (/api/v2/agent/process/stream) instead.
/// This controller will be removed in a future release.
/// </summary>
[ApiController]
[Route("api/v2/agent")]
[Authorize]
[Obsolete("Use StreamingAgentController instead. This controller will be removed in a future release.")]
public class ConversationAwareAgentController : BaseController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConversationTurnOrchestrator _turnOrchestrator;
    private readonly ISchemaCache _schemaCache;
    private readonly ITokenQuotaService _tokenQuotaService;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly new ILogger<ConversationAwareAgentController> _logger;

    public ConversationAwareAgentController(
        IUnitOfWork unitOfWork,
        ConversationTurnOrchestrator turnOrchestrator,
        ISchemaCache schemaCache,
        ITokenQuotaService tokenQuotaService,
        IConnectionEncryptionService encryptionService,
        ILogger<ConversationAwareAgentController> logger) : base(logger)
    {
        _unitOfWork = unitOfWork;
        _turnOrchestrator = turnOrchestrator;
        _schemaCache = schemaCache;
        _tokenQuotaService = tokenQuotaService;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Process message with full conversation context
    /// </summary>
    [HttpPost("process")]
    public async Task<IActionResult> ProcessMessage([FromBody] ConversationAwareProcessRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (string.IsNullOrEmpty(request?.Question))
            {
                return BadRequest(new { error = "Question is required" });
            }

            if (string.IsNullOrEmpty(request.ConnectionId))
            {
                return BadRequest(new { error = "ConnectionId is required" });
            }

            _logger.LogInformation("Processing conversation-aware message for user {UserId}, connection {ConnectionId}, conversation {ConversationId}: {Question}",
                userId, request.ConnectionId, request.ConversationId ?? "new", request.Question);

            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(request.ConnectionId, userId);
            if (connection == null)
            {
                return NotFound(new { error = "Connection not found" });
            }

            // ✅ P0: Validate schema is loaded
            var schema = await _schemaCache.GetAsync(request.ConnectionId);

            if (schema == null)
            {
                _logger.LogWarning("Schema not loaded for connection {ConnectionId}, user {UserId}", request.ConnectionId, userId);

                return BadRequest(new
                {
                    error = "SCHEMA_NOT_LOADED",
                    message = "Database schema not loaded. Please test connection first to load schema.",
                    action = "TEST_CONNECTION",
                    connectionId = request.ConnectionId,
                    suggestion = "Click 'Test Connection' button to load database schema"
                });
            }

            Conversation? dbConversation = null;
            List<Message> conversationHistory = new();
            SerializableConversationContext? persistedContext = null;
            if (!string.IsNullOrEmpty(request.ConversationId))
            {
                dbConversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(request.ConversationId, userId);
                if (dbConversation == null)
                {
                    return NotFound(new { error = "Conversation not found" });
                }

                if (!string.Equals(dbConversation.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
                {
                    return BadRequest(new
                    {
                        error = "CONVERSATION_CONNECTION_MISMATCH",
                        message = "Conversation does not belong to the provided connection."
                    });
                }

                conversationHistory = dbConversation.Messages
                    .OrderBy(m => m.CreatedAt)
                    .ToList();
                persistedContext = DeserializeConversationContext(dbConversation.ContextJson);

                _logger.LogInformation("Loaded {Count} messages from conversation history", conversationHistory.Count);
            }

            // ✅ CRIT-2 + MULTI-DB: Use DatabaseConfigContext to override both connection string AND provider
            var connectionString = BuildConnectionString(connection);
            var dbProvider = TextToSqlAgent.Infrastructure.Extensions.ConnectionExtensions.GetDatabaseProvider(connection);
            using (DatabaseConfigContext.SetDatabaseContext(connectionString, dbProvider))
            {
                // Get the orchestrator with intent routing
                var turnResult = await _turnOrchestrator.ExecuteAsync(
                    new ConversationTurnRequest
                    {
                        UserQuestion = request.Question,
                        ConnectionId = request.ConnectionId,
                        ConversationId = request.ConversationId,
                        ConversationHistory = conversationHistory,
                        PersistedContext = persistedContext
                    },
                    HttpContext.RequestAborted);

                // ✅ USE NEW INTENT ROUTING - Returns UnifiedPipelineResponse
                var unifiedResponse = turnResult.Response;

                // Ensure conversation ID is set
                var conversationId = turnResult.ConversationId;
                if (dbConversation == null)
                {
                    dbConversation = new Conversation
                    {
                        Id = conversationId,
                        UserId = userId,
                        ConnectionId = request.ConnectionId,
                        Title = BuildConversationTitle(request.Question),
                        ContextJson = JsonSerializer.Serialize(turnResult.UpdatedContext),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        LastActiveAt = DateTime.UtcNow,
                        IsArchived = false
                    };

                    await _unitOfWork.Conversations.AddAsync(dbConversation);
                }
                else
                {
                    dbConversation.ContextJson = JsonSerializer.Serialize(turnResult.UpdatedContext);
                    dbConversation.LastActiveAt = DateTime.UtcNow;
                    dbConversation.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.Conversations.UpdateAsync(dbConversation);
                }

                // Save user message to database
                var userMessage = new Message
                {
                    ConversationId = conversationId,
                    Role = "user",
                    Content = request.Question,
                    Success = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Messages.AddAsync(userMessage);

                // Extract data from UnifiedPipelineResponse based on pipeline type
                string answer = unifiedResponse.Message;
                string? sqlGenerated = unifiedResponse.SqlGenerated;
                string? queryExplanation = null;
                List<string>? processingSteps = unifiedResponse.Execution?.ProcessingSteps?.ToList();
                List<string>? suggestedQueries = null;
                SqlExecutionResult? queryResult = null;
                bool success = unifiedResponse.Success;

                // Extract pipeline-specific data
                if (unifiedResponse.Data is QueryPipelineData queryData)
                {
                    answer = queryData.Answer;
                    queryExplanation = queryData.QueryExplanation;
                    suggestedQueries = queryData.SuggestedQueries?.ToList();
                    queryResult = queryData.QueryResult;
                }
                else if (unifiedResponse.Data is WritePipelineData writeData)
                {
                    if (writeData.Result != null)
                    {
                        answer = $"Write operation completed: {writeData.Result.ActualAffectedRows} rows affected";
                    }
                }
                else if (unifiedResponse.Data is DdlPipelineData ddlData)
                {
                    if (ddlData.Result != null)
                    {
                        answer = $"DDL operation completed: {ddlData.Result.OperationType}";
                    }
                }

                // Estimate token usage for the AI response
                var inputTokens = EstimateTokens(request.Question + GetConversationContextTokens(conversationHistory));
                var outputTokens = EstimateTokens(answer + (sqlGenerated ?? "") + (queryExplanation ?? ""));
                var totalTokens = inputTokens + outputTokens;
                var model = "gpt-4o";

                // Save assistant message to database with enhanced context
                var assistantMessage = new Message
                {
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = answer,
                    SqlQuery = sqlGenerated,
                    Results = queryResult?.Rows != null ?
                        System.Text.Json.JsonSerializer.Serialize(queryResult.Rows) : null,
                    RowCount = queryResult?.RowCount,
                    ErrorMessage = unifiedResponse.Error?.Message,
                    QueryExplanation = queryExplanation,
                    ProcessingSteps = processingSteps?.Count > 0 ?
                        System.Text.Json.JsonSerializer.Serialize(processingSteps) : null,
                    SuggestedQueries = suggestedQueries?.Count > 0 ?
                        System.Text.Json.JsonSerializer.Serialize(suggestedQueries) : null,
                    CorrectionHistory = null, // Not available in UnifiedPipelineResponse
                    WasCorrected = unifiedResponse.Execution?.WasCorrected ?? false,
                    CorrectionAttempts = unifiedResponse.Execution?.CorrectionAttempts ?? 0,
                    Success = success,
                    // Token usage tracking
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = totalTokens,
                    Model = model,
                    Cost = CalculateEstimatedCost(totalTokens, model),
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Messages.AddAsync(assistantMessage);
                await _unitOfWork.SaveChangesAsync();

                // Update user quota
                try
                {
                    await _tokenQuotaService.ConsumeTokenAsync(userId, inputTokens, outputTokens, model);
                    _logger.LogInformation("Updated quota for user {UserId}: {InputTokens} input + {OutputTokens} output = {TotalTokens} total tokens",
                        userId, inputTokens, outputTokens, totalTokens);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update token quota for user {UserId}", userId);
                }

                // Format enhanced response - Return UnifiedPipelineResponse directly
                return Ok(unifiedResponse);
            } // ← DatabaseConfigContext auto-restores here via IDisposable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during conversation-aware message processing for connection {ConnectionId}", request?.ConnectionId);
            return this.CreateProblemDetails("Failed to process message with conversation context", 500);
        }
    }

    /// <summary>
    /// Get conversation summary and context
    /// </summary>
    [HttpGet("conversation/{conversationId}/context")]
    public async Task<IActionResult> GetConversationContext(string conversationId)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var messages = await _unitOfWork.Messages.GetByConversationIdAsync(conversationId);
            var conversationMessages = messages.OrderBy(m => m.CreatedAt).ToList();

            if (!conversationMessages.Any())
            {
                return NotFound(new { error = "Conversation not found" });
            }

            var context = new ConversationContextResponse
            {
                ConversationId = conversationId,
                MessageCount = conversationMessages.Count,
                TurnCount = conversationMessages.Count / 2,
                StartedAt = conversationMessages.First().CreatedAt,
                LastActiveAt = conversationMessages.Last().CreatedAt,
                Topics = ExtractTopics(conversationMessages),
                QueriesExecuted = conversationMessages.Count(m => !string.IsNullOrEmpty(m.SqlQuery)),
                TotalTokensUsed = conversationMessages.Sum(m => m.TotalTokens ?? 0),
                TotalCost = conversationMessages.Sum(m => m.Cost ?? 0)
            };

            return Ok(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation context for {ConversationId}", conversationId);
            return this.CreateProblemDetails("Failed to get conversation context", 500);
        }
    }

    private string BuildConnectionString(Connection connection)
    {
        // Get connection string with backward compatibility
        return _encryptionService.GetConnectionString(connection);
    }

    private SerializableConversationContext? DeserializeConversationContext(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SerializableConversationContext>(contextJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[ConversationAwareAgentController] Failed to deserialize persisted conversation context");
            return null;
        }
    }

    private static string BuildConversationTitle(string question)
    {
        var normalized = question.Trim();
        if (normalized.Length <= 80)
        {
            return normalized;
        }

        return normalized[..77] + "...";
    }

    private static int EstimateTokens(string text)
    {
        return text.Length / 4; // Rough estimation: 4 chars per token
    }

    private static string GetConversationContextTokens(List<Message> history)
    {
        return string.Join(" ", history.TakeLast(10).Select(m => m.Content));
    }

    private static decimal CalculateEstimatedCost(int totalTokens, string model)
    {
        // GPT-4o pricing (approximate)
        return model.ToLower() switch
        {
            "gpt-4o" => totalTokens * 0.00003m, // $0.03 per 1K tokens
            "gpt-4o-mini" => totalTokens * 0.00015m, // $0.15 per 1K tokens
            _ => totalTokens * 0.00002m // Default pricing
        };
    }

    private static List<string> ExtractTopics(List<Message> messages)
    {
        // Simple topic extraction - can be enhanced with NLP
        var topics = new HashSet<string>();

        foreach (var message in messages.Where(m => m.Role == "user"))
        {
            var words = message.Content.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words.Where(w => w.Length > 4))
            {
                topics.Add(word);
            }
        }

        return topics.Take(10).ToList();
    }
}

/// <summary>
/// Enhanced request model with conversation awareness
/// </summary>
public class ConversationAwareProcessRequest
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public bool IncludeFullHistory { get; set; } = true;
    public int MaxHistoryMessages { get; set; } = 20;
}

/// <summary>
/// Enhanced response model with conversation context
/// </summary>
public class ConversationAwareProcessResponse
{
    public bool Success { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? SqlGenerated { get; set; }
    public QueryResultDto? QueryResult { get; set; }
    public List<string> ProcessingSteps { get; set; } = new();
    public string? QueryExplanation { get; set; }
    public List<string> SuggestedQueries { get; set; } = new();
    public List<CorrectionAttemptDto> CorrectionHistory { get; set; } = new();
    public bool WasCorrected { get; set; }
    public int CorrectionAttempts { get; set; }
    public string? ConversationId { get; set; }
    public bool IsFollowUp { get; set; }
    public string? ErrorMessage { get; set; }
    public string ConnectionId { get; set; } = string.Empty;

    // Enhanced conversation context
    public int ConversationTurns { get; set; }
    public bool HasConversationContext { get; set; }
    public DateTime? ConversationStartedAt { get; set; }

    // ✅ NEW: Context awareness for pronoun resolution
    public List<string> ContextEntities { get; set; } = new();
    public string? PrimaryEntity { get; set; }
    public bool PronounsResolved { get; set; }
}

/// <summary>
/// Conversation context response
/// </summary>
public class ConversationContextResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int TurnCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public List<string> Topics { get; set; } = new();
    public int QueriesExecuted { get; set; }
    public int TotalTokensUsed { get; set; }
    public decimal TotalCost { get; set; }
}
