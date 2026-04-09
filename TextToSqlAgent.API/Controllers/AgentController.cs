using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// [DEPRECATED] Legacy controller for AI Agent processing.
/// Use StreamingAgentController (/api/v2/agent/process/stream) instead.
/// This controller will be removed in a future release.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Obsolete("Use StreamingAgentController instead. This controller will be removed in a future release.")]
public class AgentController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly ILogger<AgentController> _logger;
    private readonly TextToSqlAgent.Infrastructure.Services.ITokenQuotaService _tokenQuotaService;

    public AgentController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IConnectionEncryptionService encryptionService,
        ILogger<AgentController> logger,
        TextToSqlAgent.Infrastructure.Services.ITokenQuotaService tokenQuotaService)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _logger = logger;
        _tokenQuotaService = tokenQuotaService;
    }

    /// <summary>
    /// Process a message using the Enhanced Agent Orchestrator
    /// </summary>
    [HttpPost("process")]
    public async Task<IActionResult> ProcessMessage([FromBody] ProcessMessageRequest request)
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

            // ✅ Validate question doesn't contain image input (not supported by current model)
            var imageValidation = ValidateQuestionInput(request.Question);
            if (!imageValidation.IsValid)
            {
                return BadRequest(new { error = "INPUT_NOT_SUPPORTED", message = imageValidation.ErrorMessage });
            }

            _logger.LogInformation("Processing message for user {UserId}, connection {ConnectionId}: {Question}",
                userId, request.ConnectionId, request.Question);

            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(request.ConnectionId, userId);
            if (connection == null)
            {
                return NotFound(new { error = "Connection not found" });
            }

            // ✅ P0: Validate schema is loaded
            var schemaCache = _serviceProvider.GetRequiredService<ISchemaCache>();
            var schema = await schemaCache.GetAsync(request.ConnectionId);

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

            // Create a scoped service provider with overridden database configuration
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            // ✅ TASK 2.3: Load conversation history if conversationId is provided
            List<Message>? conversationHistory = null;
            if (!string.IsNullOrEmpty(request.ConversationId))
            {
                var messages = await _unitOfWork.Messages.GetByConversationIdAsync(request.ConversationId);
                if (messages != null && messages.Any())
                {
                    conversationHistory = messages.OrderBy(m => m.CreatedAt).ToList();
                    _logger.LogInformation(
                        "[AgentController] Loaded {Count} messages from conversation {ConversationId}",
                        conversationHistory.Count, request.ConversationId);
                }
            }

            // ✅ CRIT-2 FIX: Use DatabaseConfigContext.SetConnectionString() instead of mutating Singleton
            var connectionString = BuildConnectionString(connection);
            using (DatabaseConfigContext.SetConnectionString(connectionString))
            {
                // Get the enhanced agent orchestrator from scoped DI
                var agent = scopedServices.GetRequiredService<EnhancedAgentOrchestrator>();

                // ✅ USE INTENT ROUTING - Returns UnifiedPipelineResponse
                var unifiedResponse = await agent.ProcessMessageWithIntentRoutingAsync(
                    request.Question,
                    request.ConnectionId,
                    request.ConversationId,
                    conversationHistory, // ✅ TASK 2.3: Pass loaded conversation history
                    progress: null,  // No SSE streaming for this endpoint
                    sqlTokenCallback: null,
                    CancellationToken.None);

                // Save user message to database
                var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
                var userMessage = new TextToSqlAgent.Infrastructure.Entities.Message
                {
                    ConversationId = conversationId,
                    Role = "user",
                    Content = request.Question,
                    Success = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Messages.AddAsync(userMessage);

                // Extract data from UnifiedPipelineResponse
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

                // Estimate token usage for the AI response
                var inputTokens = EstimateTokens(request.Question);
                var outputTokens = EstimateTokens(answer + (sqlGenerated ?? "") + (queryExplanation ?? ""));
                var totalTokens = inputTokens + outputTokens;
                var model = "gpt-4o";

                // Save assistant message to database with token usage
                var assistantMessage = new TextToSqlAgent.Infrastructure.Entities.Message
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
                    CorrectionHistory = null,
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

                // Update user quota using TokenQuotaService
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

                // ✅ Return UnifiedPipelineResponse directly    
                return Ok(unifiedResponse);
            } // ← DatabaseConfigContext auto-restores here via IDisposable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during message processing for connection {ConnectionId}", request?.ConnectionId);
            return this.CreateProblemDetails("Failed to process message", 500);
        }
    }

    /// <summary>
    /// Build connection string from connection entity
    /// </summary>
    private string BuildConnectionString(Connection connection)
    {
        // Get connection string with backward compatibility
        return _encryptionService.GetConnectionString(connection);
    }

    /// <summary>
    /// Validates question input for unsupported formats (images, files)
    /// Current model (gpt-4o) does not support vision/multimodal input
    /// </summary>
    private static InputValidationResult ValidateQuestionInput(string question)
    {
        if (string.IsNullOrEmpty(question))
        {
            return new InputValidationResult { IsValid = true };
        }

        var lowerQuestion = question.ToLowerInvariant();

        // Check for image URLs
        if (question.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg" };
            foreach (var ext in imageExtensions)
            {
                if (lowerQuestion.Contains(ext))
                {
                    return new InputValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Image input is not supported. The current AI model does not support image analysis. Please describe your question in text format."
                    };
                }
            }
        }

        // Check for base64 image data
        if (question.Contains("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return new InputValidationResult
            {
                IsValid = false,
                ErrorMessage = "Image input is not supported. The current AI model does not support image analysis. Please describe your question in text format."
            };
        }

        // Check for image-related keywords
        var imageKeywords = new[] { "analyze this image", "look at this", "what's in this picture", 
            "describe the image", "what does this show", "read this image", "extract text from image",
            "ocr", "convert image to text" };
        
        foreach (var keyword in imageKeywords)
        {
            if (lowerQuestion.Contains(keyword))
            {
                return new InputValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Image analysis is not supported. The current AI model does not support vision/multimodal input. Please ask a text-based database question."
                };
            }
        }

        return new InputValidationResult { IsValid = true };
    }

    /// <summary>
    /// Input validation result
    /// </summary>
    private class InputValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Estimate token count for text (rough approximation: 1 token ≈ 4 characters)
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough approximation: 1 token ≈ 4 characters for English text
        // This is a simplified estimation - real tokenizers are more complex
        return Math.Max(1, text.Length / 4);
    }

    /// <summary>
    /// Calculate estimated cost based on token count and model
    /// </summary>
    private static decimal CalculateEstimatedCost(int totalTokens, string model)
    {
        // Approximate pricing per 1K tokens (can be adjusted based on actual model pricing)
        var costPer1KTokens = model.ToLowerInvariant() switch
        {
            var m when m.Contains("gpt-4") => 0.03m, // $0.03 per 1K tokens
            var m when m.Contains("gpt-3.5") => 0.002m, // $0.002 per 1K tokens
            var m when m.Contains("gemini") => 0.00025m, // $0.00025 per 1K tokens
            _ => 0.03m // Default to GPT-4 pricing
        };

        return (totalTokens / 1000m) * costPer1KTokens;
    }
}

/// <summary>
/// Request model for processing a message
/// </summary>
public class ProcessMessageRequest
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
}

/// <summary>
/// Response model for processed message
/// </summary>
public class ProcessMessageResponse
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

    // ✅ NEW: Pipeline and Intent metadata for frontend
    public string? Pipeline { get; set; }
    public string? Intent { get; set; }
    public bool IsWriteOperation { get; set; }
    public bool IsDdlOperation { get; set; }
    public bool IsForbiddenOperation { get; set; }
    public bool RequiresConfirmation { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// DTO for query execution results
/// </summary>
public class QueryResultDto
{
    public bool Success { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int RowCount { get; set; }
    public int ExecutionTimeMs { get; set; }
    public int RowsAffected { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// DTO for correction attempts
/// </summary>
public class CorrectionAttemptDto
{
    public string OriginalSql { get; set; } = string.Empty;
    public string CorrectedSql { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime Timestamp { get; set; }
}