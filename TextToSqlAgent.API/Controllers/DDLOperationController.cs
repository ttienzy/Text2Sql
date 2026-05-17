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
/// Controller for DDL operations (CREATE INDEX, ALTER TABLE, CREATE VIEW/PROC) with impact analysis
/// Returns UnifiedPipelineResponse for consistency
/// </summary>
[ApiController]
[Route("api/agent/ddl")]
[Authorize]
public class DDLOperationController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionEncryptionService _encryptionService;
    private readonly PipelineResponseBuilder _responseBuilder;
    private readonly ILogger<DDLOperationController> _logger;

    public DDLOperationController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IConnectionEncryptionService encryptionService,
        PipelineResponseBuilder responseBuilder,
        ILogger<DDLOperationController> logger)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _encryptionService = encryptionService;
        _responseBuilder = responseBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Generate preview with impact analysis (Steps D1-D6)
    /// Returns UnifiedPipelineResponse
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] DDLOperationRequest request)
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

            // ✅ CRIT-2 + MULTI-DB: Override both connection string AND provider per-request
            var connectionString = BuildConnectionString(connection);
            var dbProvider = TextToSqlAgent.Infrastructure.Extensions.ConnectionExtensions.GetDatabaseProvider(connection);
            using (DatabaseConfigContext.SetDatabaseContext(connectionString, dbProvider))
            {
                var ddlPipeline = scopedServices.GetRequiredService<IDDLPipeline>();

                // Generate preview with impact analysis
                var preview = await ddlPipeline.GeneratePreviewAsync(request);

                // Create intent result for response builder
                var intentResult = new IntentClassificationResult
                {
                    Intent = MapDDLTypeToIntent(preview.OperationType),
                    Route = PipelineRoute.Ddl,
                    Confidence = 1.0,
                    DetectedEntities = new List<string> { preview.TargetObject },
                    MatchedKeywords = new List<string>()
                };

                // Build unified response
                var response = _responseBuilder.BuildDdlPreviewResponse(preview, intentResult, stopwatch);

                return Ok(response);
            } // ← DatabaseConfigContext auto-restores here via IDisposable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DDLOperation] Error generating preview");
            var errorResponse = _responseBuilder.BuildErrorResponse(ex, PipelineType.Ddl);
            return StatusCode(500, errorResponse);
        }
    }

    /// <summary>
    /// Execute DDL operation after user confirmation (Steps D7-D8)
    /// Returns UnifiedPipelineResponse
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

            // ✅ CRIT-2 + MULTI-DB: Override both connection string AND provider per-request
            var connectionString = BuildConnectionString(connection);
            var dbProvider = TextToSqlAgent.Infrastructure.Extensions.ConnectionExtensions.GetDatabaseProvider(connection);
            using (DatabaseConfigContext.SetDatabaseContext(connectionString, dbProvider))
            {
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

                // Save to message history
                await SaveDDLOperationToHistory(
                    request.ConversationId,
                    request.Question,
                    result);

                // Create intent result for response builder
                var intentResult = new IntentClassificationResult
                {
                    Intent = MapDDLTypeToIntent(result.OperationType),
                    Route = PipelineRoute.Ddl,
                    Confidence = 1.0,
                    DetectedEntities = new List<string> { result.TargetObject },
                    MatchedKeywords = new List<string>()
                };

                // Build unified response
                var response = _responseBuilder.BuildDdlResultResponse(result, intentResult);

                return Ok(response);
            } // ← DatabaseConfigContext auto-restores here via IDisposable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DDLOperation] Error executing DDL operation");
            var errorResponse = _responseBuilder.BuildErrorResponse(ex, PipelineType.Ddl);
            return StatusCode(500, errorResponse);
        }
    }

    private IntentCategory MapDDLTypeToIntent(DDLOperationType ddlType)
    {
        return ddlType switch
        {
            DDLOperationType.CreateIndex or DDLOperationType.DropIndex => IntentCategory.DdlIndex,
            DDLOperationType.CreateProcedure or DDLOperationType.AlterProcedure or
            DDLOperationType.CreateFunction or DDLOperationType.AlterFunction => IntentCategory.DdlProcedure,
            DDLOperationType.CreateView or DDLOperationType.AlterView => IntentCategory.DdlView,
            DDLOperationType.AlterTableAddColumn or DDLOperationType.AlterTableModifyColumn or
            DDLOperationType.AlterTableDropColumn => IntentCategory.DdlAlter,
            _ => IntentCategory.Unknown
        };
    }

    private string BuildConnectionString(Connection connection)
    {
        // Get connection string with backward compatibility
        return _encryptionService.GetConnectionString(connection);
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
