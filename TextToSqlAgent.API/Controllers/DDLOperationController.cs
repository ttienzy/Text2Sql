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
/// Controller for DDL operations (CREATE INDEX, ALTER TABLE, CREATE VIEW/PROC) with impact analysis
/// </summary>
[ApiController]
[Route("api/agent/ddl")]
[Authorize]
public class DDLOperationController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly ILogger<DDLOperationController> _logger;

    public DDLOperationController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IConnectionEncryptionService encryptionService,
        ILogger<DDLOperationController> logger)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Generate preview with impact analysis (Steps D1-D6)
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] DDLOperationRequest request)
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
                "[DDLOperation] Generating preview for user {UserId}: {Question}",
                userId, request.Question);

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

            var ddlPipeline = scopedServices.GetRequiredService<IDDLPipeline>();

            // Generate preview with impact analysis
            var preview = await ddlPipeline.GeneratePreviewAsync(request);

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
                message = "Please review the impact analysis and confirm this DDL operation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DDLOperation] Error generating preview");
            return StatusCode(500, new { error = $"Error generating preview: {ex.Message}" });
        }
    }

    /// <summary>
    /// Execute DDL operation after user confirmation (Steps D7-D8)
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] DDLExecutionRequest request)
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
                "[DDLOperation] Executing {Type} on {Target} for user {UserId}",
                request.Preview.OperationType, request.Preview.TargetObject, userId);

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

            var ddlPipeline = scopedServices.GetRequiredService<IDDLPipeline>();

            // Execute with confirmation
            var operationRequest = new DDLOperationRequest
            {
                Question = request.Question,
                ConnectionId = request.ConnectionId,
                ConversationId = request.ConversationId,
                IsConfirmed = true
            };

            var result = await ddlPipeline.ExecuteAsync(operationRequest, request.Preview);

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
            await SaveDDLOperationToHistory(
                request.ConversationId,
                request.Question,
                result);

            return Ok(new
            {
                success = true,
                result = result,
                message = $"Successfully executed {result.OperationType} on {result.TargetObject}",
                schema_reloaded = result.SchemaCacheReloaded
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DDLOperation] Error executing DDL operation");
            return StatusCode(500, new { error = $"Execution failed: {ex.Message}" });
        }
    }

    private string BuildConnectionString(Connection connection)
    {
        // Decrypt the connection string (it's stored encrypted)
        return _encryptionService.DecryptPassword(connection.ConnectionString, connection.Id);
    }

    private async Task SaveDDLOperationToHistory(
        string? conversationId,
        string question,
        DDLOperationResult result)
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
                Content = $"Executed {result.OperationType} on {result.TargetObject}",
                SqlQuery = result.DDLExecuted,
                Success = result.Success,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Messages.AddAsync(assistantMessage);

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DDLOperation] Failed to save to history");
        }
    }
}

public class DDLExecutionRequest
{
    public string Question { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public bool Confirmed { get; set; }
    public DDLOperationPreview Preview { get; set; } = null!;
}
