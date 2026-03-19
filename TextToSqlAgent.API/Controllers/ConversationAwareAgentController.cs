using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.Infrastructure.Services;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Enhanced Agent Controller with conversation awareness
/// Maintains context across multiple turns in a conversation
/// </summary>
[ApiController]
[Route("api/v2/agent")]
[Authorize]
public class ConversationAwareAgentController : BaseController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITokenQuotaService _tokenQuotaService;
    private readonly new ILogger<ConversationAwareAgentController> _logger;

    public ConversationAwareAgentController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ITokenQuotaService tokenQuotaService,
        ILogger<ConversationAwareAgentController> logger) : base(logger)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _tokenQuotaService = tokenQuotaService;
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

            // Get conversation history if conversation ID is provided
            List<Message> conversationHistory = new();
            if (!string.IsNullOrEmpty(request.ConversationId))
            {
                conversationHistory = (await _unitOfWork.Messages.GetByConversationIdAsync(request.ConversationId))
                    .OrderBy(m => m.CreatedAt)
                    .ToList();

                _logger.LogInformation("Loaded {Count} messages from conversation history", conversationHistory.Count);
            }

            // Create a scoped service provider with overridden database configuration
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            // Get the database config and temporarily override it for this connection
            var dbConfig = scopedServices.GetRequiredService<DatabaseConfig>();
            var originalConnectionString = dbConfig.ConnectionString;

            // Build connection string from connection entity
            var connectionString = BuildConnectionString(connection);
            dbConfig.ConnectionString = connectionString;

            try
            {
                // Get the working orchestrator instead of conversation-aware one
                var orchestrator = scopedServices.GetRequiredService<EnhancedAgentOrchestrator>();

                // Process the query using existing working orchestrator with conversation history
                var response = await orchestrator.ProcessQueryAsync(
                    request.Question,
                    request.ConversationId,
                    conversationHistory,  // ✅ Pass conversation history for context awareness
                    CancellationToken.None);

                // Ensure conversation ID is set
                var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
                response.ConversationId = conversationId;

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

                // Estimate token usage for the AI response
                var inputTokens = EstimateTokens(request.Question + GetConversationContextTokens(conversationHistory));
                var outputTokens = EstimateTokens(response.Answer + (response.SqlGenerated ?? "") + (response.QueryExplanation ?? ""));
                var totalTokens = inputTokens + outputTokens;
                var model = "gpt-4o";

                // Save assistant message to database with enhanced context
                var assistantMessage = new Message
                {
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = response.Answer,
                    SqlQuery = response.SqlGenerated,
                    Results = response.QueryResult?.Rows != null ?
                        System.Text.Json.JsonSerializer.Serialize(response.QueryResult.Rows) : null,
                    RowCount = response.QueryResult?.RowCount,
                    ErrorMessage = response.ErrorMessage,
                    QueryExplanation = response.QueryExplanation,
                    ProcessingSteps = response.ProcessingSteps?.Count > 0 ?
                        System.Text.Json.JsonSerializer.Serialize(response.ProcessingSteps) : null,
                    SuggestedQueries = response.SuggestedQueries?.Count > 0 ?
                        System.Text.Json.JsonSerializer.Serialize(response.SuggestedQueries) : null,
                    CorrectionHistory = response.CorrectionHistory?.Count > 0 ?
                        System.Text.Json.JsonSerializer.Serialize(response.CorrectionHistory) : null,
                    WasCorrected = response.WasCorrected,
                    CorrectionAttempts = response.CorrectionAttempts,
                    Success = response.Success,
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

                // Format enhanced response
                var result = new ConversationAwareProcessResponse
                {
                    Success = response.Success,
                    Question = request.Question,
                    Answer = response.Answer,
                    SqlGenerated = response.SqlGenerated,
                    QueryResult = response.QueryResult != null ? new QueryResultDto
                    {
                        Success = response.QueryResult.Success,
                        Columns = response.QueryResult.Columns,
                        Rows = response.QueryResult.Rows,
                        RowCount = response.QueryResult.RowCount,
                        ExecutionTimeMs = (int)response.QueryResult.ExecutionTimeMs,
                        RowsAffected = response.QueryResult.RowsAffected,
                        ErrorMessage = response.QueryResult.ErrorMessage
                    } : null,
                    ProcessingSteps = response.ProcessingSteps,
                    QueryExplanation = response.QueryExplanation,
                    SuggestedQueries = response.SuggestedQueries,
                    CorrectionHistory = response.CorrectionHistory?.Select(c => new CorrectionAttemptDto
                    {
                        OriginalSql = c.OriginalSql,
                        CorrectedSql = c.CorrectedSql,
                        Error = c.Error?.ErrorMessage,
                        Reasoning = c.Reasoning,
                        Success = c.Success,
                        AttemptNumber = c.AttemptNumber,
                        Timestamp = c.Timestamp
                    }).ToList() ?? new List<CorrectionAttemptDto>(),
                    WasCorrected = response.WasCorrected,
                    CorrectionAttempts = response.CorrectionAttempts,
                    ConversationId = conversationId,
                    IsFollowUp = response.IsFollowUp,
                    ErrorMessage = response.ErrorMessage,
                    ConnectionId = request.ConnectionId,
                    // Enhanced conversation context
                    ConversationTurns = conversationHistory.Count / 2, // Approximate turns
                    HasConversationContext = conversationHistory.Any(),
                    ConversationStartedAt = conversationHistory.FirstOrDefault()?.CreatedAt,
                    // ✅ Context awareness for pronoun resolution
                    ContextEntities = response.ContextEntities,
                    PrimaryEntity = response.PrimaryEntity,
                    PronounsResolved = response.PronounsResolved
                };

                return Ok(result);
            }
            finally
            {
                // Restore original connection string
                dbConfig.ConnectionString = originalConnectionString;
            }
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
        // Implementation same as original AgentController
        return $"Server={connection.Host};Database={connection.Database};User Id={connection.Username};Password={DecryptPassword(connection.EncryptedPassword)};TrustServerCertificate=True;";
    }

    private string DecryptPassword(string encryptedPassword)
    {
        // For now, return as-is. In production, implement proper decryption
        // This should use the same encryption service used when storing the password
        return encryptedPassword;
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