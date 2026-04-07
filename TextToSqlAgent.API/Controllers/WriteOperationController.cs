using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for WRITE operations (INSERT/UPDATE) with mandatory confirmation
/// Returns UnifiedPipelineResponse for consistency
/// </summary>
[ApiController]
[Route("api/agent/write")]
[Authorize]
public class WriteOperationController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly PipelineResponseBuilder _responseBuilder;
    private readonly ILogger<WriteOperationController> _logger;

    public WriteOperationController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IConnectionEncryptionService encryptionService,
        PipelineResponseBuilder responseBuilder,
        ILogger<WriteOperationController> logger)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _responseBuilder = responseBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Generate preview of write operation (Step W1-W7)
    /// Returns UnifiedPipelineResponse
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] WriteOperationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

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

            _logger.LogInformation(
                "[WriteOperation] Generating preview for user {UserId}: {Question}",
                userId, request.Question);

            // Verify user owns the connection
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(request.ConnectionId, userId);
            if (connection == null)
            {
                return NotFound(new { error = "Connection not found" });
            }

            // Create scoped service provider with connection override
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            var dbConfig = scopedServices.GetRequiredService<DatabaseConfig>();
            dbConfig.ConnectionString = BuildConnectionString(connection);

            var writePipeline = scopedServices.GetRequiredService<IWritePipeline>();
            var intentClassifier = scopedServices.GetService<IIntentClassifier>();

            // Generate preview
            var preview = await writePipeline.GeneratePreviewAsync(request);

            // Create intent result for response builder
            var intentResult = new IntentClassificationResult
            {
                Intent = preview.OperationType == WriteOperationType.Insert
                    ? IntentCategory.Insert
                    : IntentCategory.Update,
                Route = PipelineRoute.Write,
                Confidence = 1.0,
                DetectedEntities = new List<string> { preview.TargetTable },
                MatchedKeywords = new List<string>()
            };

            // Build unified response
            var response = _responseBuilder.BuildWritePreviewResponse(preview, intentResult, stopwatch);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WriteOperation] Error generating preview");
            var errorResponse = _responseBuilder.BuildErrorResponse(ex, PipelineType.Write);
            return StatusCode(500, errorResponse);
        }
    }

    /// <summary>
    /// Execute write operation after user confirmation (Step W8-W9)
    /// Returns UnifiedPipelineResponse
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] WriteExecutionRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (request?.Preview == null)
            {
                return BadRequest(new { error = "Preview is required" });
            }

            if (!request.Confirmed)
            {
                return BadRequest(new { error = "User confirmation is required" });
            }

            _logger.LogInformation(
                "[WriteOperation] Executing {Type} on {Table} for user {UserId}",
                request.Preview.OperationType, request.Preview.TargetTable, userId);

            // Verify connection ownership
            var connection = await _unitOfWork.Connections.GetByIdAndUserIdAsync(request.ConnectionId, userId);
            if (connection == null)
            {
                return NotFound(new { error = "Connection not found" });
            }

            // Create scoped service provider
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            var dbConfig = scopedServices.GetRequiredService<DatabaseConfig>();
            dbConfig.ConnectionString = BuildConnectionString(connection);

            var writePipeline = scopedServices.GetRequiredService<IWritePipeline>();

            // Execute with confirmation
            var operationRequest = new WriteOperationRequest
            {
                Question = request.Question,
                ConnectionId = request.ConnectionId,
                ConversationId = request.ConversationId,
                IsConfirmed = true
            };

            var result = await writePipeline.ExecuteAsync(operationRequest, request.Preview);

            // Save to message history
            await SaveWriteOperationToHistory(
                request.ConversationId,
                request.Question,
                result);

            // Create intent result for response builder
            var intentResult = new IntentClassificationResult
            {
                Intent = result.OperationType == WriteOperationType.Insert
                    ? IntentCategory.Insert
                    : IntentCategory.Update,
                Route = PipelineRoute.Write,
                Confidence = 1.0,
                DetectedEntities = new List<string> { result.TargetTable },
                MatchedKeywords = new List<string>()
            };

            // Build unified response
            var response = _responseBuilder.BuildWriteResultResponse(result, intentResult);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WriteOperation] Error executing write operation");
            var errorResponse = _responseBuilder.BuildErrorResponse(ex, PipelineType.Write);
            return StatusCode(500, errorResponse);
        }
    }

    private string BuildConnectionString(Connection connection)
    {
        // Get connection string with backward compatibility
        return _encryptionService.GetConnectionString(connection);
    }

    private async Task SaveWriteOperationToHistory(
        string? conversationId,
        string question,
        WriteOperationResult result)
    {
        try
        {
            var convId = conversationId ?? Guid.NewGuid().ToString();

            // Save user message
            var userMessage = new TextToSqlAgent.Infrastructure.Entities.Message
            {
                ConversationId = convId,
                Role = "user",
                Content = question,
                Success = true,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(userMessage);

            // Save assistant response
            var assistantMessage = new TextToSqlAgent.Infrastructure.Entities.Message
            {
                ConversationId = convId,
                Role = "assistant",
                Content = $"Executed {result.OperationType}: {result.ActualAffectedRows} rows affected",
                SqlQuery = result.SqlExecuted,
                Success = result.Success,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(assistantMessage);

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WriteOperation] Failed to save to history");
        }
    }
}

public class WriteExecutionRequest
{
    public string Question { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public bool Confirmed { get; set; }
    public WriteOperationPreview Preview { get; set; } = null!;
}
