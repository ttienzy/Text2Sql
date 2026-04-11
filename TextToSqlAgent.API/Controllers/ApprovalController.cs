using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// API endpoints for managing async approval queue
/// Supports the new async approval UX pattern
/// </summary>
[ApiController]
[Route("api/approvals")]
[Authorize]
public class ApprovalController : ControllerBase
{
    private readonly IApprovalQueueService _approvalService;
    private readonly IWritePipeline _writePipeline;
    private readonly ILogger<ApprovalController> _logger;

    public ApprovalController(
        IApprovalQueueService approvalService,
        IWritePipeline writePipeline,
        ILogger<ApprovalController> logger)
    {
        _approvalService = approvalService;
        _writePipeline = writePipeline;
        _logger = logger;
    }

    /// <summary>
    /// Get all pending approvals for the current user
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingApprovals(CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User ID not found" });
        }

        var approvals = await _approvalService.GetPendingApprovalsAsync(userId, ct);

        return Ok(new
        {
            count = approvals.Count,
            approvals = approvals.Select(a => new
            {
                id = a.Id,
                question = a.Question,
                operationType = a.OperationType,
                targetTable = a.TargetTable,
                sqlStatement = a.SqlStatement,
                estimatedRows = a.EstimatedRows,
                riskLevel = a.RiskLevel,
                warnings = a.Warnings,
                hasWhereClause = a.HasWhereClause,
                createdAt = a.CreatedAt,
                timeoutAt = a.TimeoutAt,
                connectionId = a.ConnectionId,
                conversationId = a.ConversationId
            })
        });
    }

    /// <summary>
    /// Get a specific approval by ID
    /// </summary>
    [HttpGet("{approvalId}")]
    public async Task<IActionResult> GetApproval(string approvalId, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User ID not found" });
        }

        var approval = await _approvalService.GetApprovalByIdAsync(approvalId, userId, ct);
        if (approval == null)
        {
            return NotFound(new { message = "Approval not found" });
        }

        return Ok(new
        {
            id = approval.Id,
            question = approval.Question,
            operationType = approval.OperationType,
            targetTable = approval.TargetTable,
            sqlStatement = approval.SqlStatement,
            estimatedRows = approval.EstimatedRows,
            riskLevel = approval.RiskLevel,
            warnings = approval.Warnings,
            hasWhereClause = approval.HasWhereClause,
            status = approval.Status,
            createdAt = approval.CreatedAt,
            timeoutAt = approval.TimeoutAt,
            respondedAt = approval.RespondedAt,
            executedAt = approval.ExecutedAt,
            connectionId = approval.ConnectionId,
            conversationId = approval.ConversationId,
            executionResult = approval.ExecutionResult,
            affectedRows = approval.AffectedRows
        });
    }

    /// <summary>
    /// Approve an approval request and execute the SQL
    /// </summary>
    [HttpPost("{approvalId}/approve")]
    public async Task<IActionResult> Approve(
        string approvalId,
        [FromBody] ApproveRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User ID not found" });
        }

        var approval = await _approvalService.GetApprovalByIdAsync(approvalId, userId, ct);
        if (approval == null)
        {
            return NotFound(new { message = "Approval not found" });
        }

        if (approval.Status != "pending")
        {
            return BadRequest(new { message = $"Cannot approve - status is {approval.Status}" });
        }

        // Mark as approved
        var success = await _approvalService.ApproveAsync(approvalId, userId, request.ModifiedSql, ct);
        if (!success)
        {
            return BadRequest(new { message = "Failed to approve" });
        }

        // Execute SQL asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await _approvalService.MarkExecutingAsync(approvalId, ct);

                var sqlToExecute = request.ModifiedSql ?? approval.SqlStatement;
                var preview = new Core.Models.WriteOperationPreview
                {
                    SqlStatement = sqlToExecute,
                    OperationType = Enum.Parse<Core.Models.WriteOperationType>(approval.OperationType),
                    TargetTable = approval.TargetTable,
                    EstimatedAffectedRows = approval.EstimatedRows,
                    RequiresConfirmation = false
                };

                var writeRequest = new Core.Models.WriteOperationRequest
                {
                    Question = approval.Question,
                    ConnectionId = approval.ConnectionId,
                    ConversationId = approval.ConversationId,
                    IsConfirmed = true
                };

                var result = await _writePipeline.ExecuteAsync(writeRequest, preview, ct);

                var executionResult = System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = result.Success,
                    affectedRows = result.ActualAffectedRows,
                    errorMessage = result.ErrorMessage,
                    executionTime = result.ExecutionTime
                });

                await _approvalService.MarkCompletedAsync(
                    approvalId,
                    executionResult,
                    result.ActualAffectedRows,
                    ct);

                _logger.LogInformation(
                    "[ApprovalController] Executed approval {Id} - Success: {Success}, Rows: {Rows}",
                    approvalId, result.Success, result.ActualAffectedRows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ApprovalController] Failed to execute approval {Id}", approvalId);

                var errorResult = System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = ex.Message
                });

                await _approvalService.MarkCompletedAsync(approvalId, errorResult, 0, ct);
            }
        }, ct);

        return Ok(new
        {
            message = "Approval accepted - executing SQL in background",
            approvalId,
            status = "executing"
        });
    }

    /// <summary>
    /// Reject an approval request
    /// </summary>
    [HttpPost("{approvalId}/reject")]
    public async Task<IActionResult> Reject(
        string approvalId,
        [FromBody] RejectRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User ID not found" });
        }

        var approval = await _approvalService.GetApprovalByIdAsync(approvalId, userId, ct);
        if (approval == null)
        {
            return NotFound(new { message = "Approval not found" });
        }

        if (approval.Status != "pending")
        {
            return BadRequest(new { message = $"Cannot reject - status is {approval.Status}" });
        }

        var success = await _approvalService.RejectAsync(approvalId, userId, request.Reason ?? "User rejected", ct);
        if (!success)
        {
            return BadRequest(new { message = "Failed to reject" });
        }

        return Ok(new
        {
            message = "Approval rejected",
            approvalId,
            status = "rejected"
        });
    }
}

public class ApproveRequest
{
    public string? ModifiedSql { get; set; }
}

public class RejectRequest
{
    public string? Reason { get; set; }
}
