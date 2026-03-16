using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for AI Agent processing - handles message processing with EnhancedAgentOrchestrator
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
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

            _logger.LogInformation("Processing message for user {UserId}, connection {ConnectionId}: {Question}",
                userId, request.ConnectionId, request.Question);

            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(request.ConnectionId, userId);
            if (connection == null)
            {
                return NotFound(new { error = "Connection not found" });
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
                // Get the enhanced agent orchestrator from scoped DI
                var agent = scopedServices.GetRequiredService<EnhancedAgentOrchestrator>();

                // Process the query using the same pipeline as Console project
                var response = await agent.ProcessQueryAsync(request.Question, request.ConversationId);

                // Save user message to database
                var userMessage = new TextToSqlAgent.Infrastructure.Entities.Message
                {
                    ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                    Role = "user",
                    Content = request.Question,
                    Success = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Messages.AddAsync(userMessage);

                // Estimate token usage for the AI response
                // This is an approximation - in a real implementation, you'd get this from the LLM API
                var inputTokens = EstimateTokens(request.Question);
                var outputTokens = EstimateTokens(response.Answer + (response.SqlGenerated ?? "") + (response.QueryExplanation ?? ""));
                var totalTokens = inputTokens + outputTokens;
                var model = "gpt-4o"; // Default model - could be configurable

                // Save assistant message to database with token usage
                var assistantMessage = new TextToSqlAgent.Infrastructure.Entities.Message
                {
                    ConversationId = userMessage.ConversationId,
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
                    // Don't fail the request if quota update fails
                }

                // Update response with conversation ID
                response.ConversationId = userMessage.ConversationId;

                // Format response for production API
                var result = new ProcessMessageResponse
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
                        ExecutionTimeMs = response.QueryResult.ExecutionTimeMs,
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
                    ConversationId = response.ConversationId,
                    IsFollowUp = response.IsFollowUp,
                    ErrorMessage = response.ErrorMessage,
                    ConnectionId = request.ConnectionId
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
            _logger.LogError(ex, "Error during message processing for connection {ConnectionId}", request?.ConnectionId);
            return this.CreateProblemDetails("Failed to process message", 500);
        }
    }

    /// <summary>
    /// Build connection string from connection entity
    /// </summary>
    private string BuildConnectionString(Connection connection)
    {
        // Decrypt the connection string (it's stored encrypted)
        return _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);
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