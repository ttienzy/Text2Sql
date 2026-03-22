using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Controller for WRITE operations (INSERT/UPDATE) with mandatory confirmation
/// </summary>
[ApiController]
[Route("api/agent/write")]
[Authorize]
public class WriteOperationController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly ILogger<WriteOperationController> _logger;

    public WriteOperationController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IConnectionEncryptionService encryptionService,
        ILogger<WriteOperationController> logger)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Generate preview of write operation (Step W1-W7)
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] WriteOperationRequest request)
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

            // Generate preview
            var preview = await writePipeline.GeneratePreviewAsync(request);

            if (!string.IsNullOrEmpty(preview.ValidationError))
            {
                return BadRequest(new
                {
                    error = preview.ValidationError,
                    preview = preview
                });
            }

            return Ok(new
            {
                success = true,
                preview = preview,
                message = "Please review and confirm this write operation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WriteOperation] Error generating preview");
            return StatusCode(500, new { error = $"Error generating preview: {ex.Message}" });
        }
    }

    /// <summary>
    /// Execute write operation after user confirmation (Step W8-W9)
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

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    error = result.ErrorMessage,
                    result = result
                });
            }

            // Save to message history
            await SaveWriteOperationToHistory(
                request.ConversationId,
                request.Question,
                result);

            return Ok(new
            {
                success = true,
                result = result,
                message = $"Successfully {result.OperationType.ToString().ToLower()}ed {result.ActualAffectedRows} row(s)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WriteOperation] Error executing write operation");
            return StatusCode(500, new { error = $"Execution failed: {ex.Message}" });
        }
    }

    private string BuildConnectionString(Connection connection)
    {
        // Decrypt the connection string (it's stored encrypted)
        return _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);
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
