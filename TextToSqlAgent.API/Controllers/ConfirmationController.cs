using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Handles user confirmation for DML/DDL operations.
/// Called by the frontend when user approves/cancels a pending SQL operation.
/// The SSE controller polls Redis for the result and resumes/cancels accordingly.
/// 
/// Flow:
///   1. SSE controller stores PendingConfirmation in Redis → emits awaiting_confirm event
///   2. Frontend shows confirm modal with SQL preview + risk badge
///   3. User clicks Approve → POST /api/agent/confirm/{id}
///      OR clicks Cancel → POST /api/agent/confirm/{id}/cancel
///   4. This controller writes ConfirmationResult to Redis
///   5. SSE controller's polling loop picks up the result and continues
/// </summary>
[ApiController]
[Route("api/agent/confirm")]
[Authorize]
public class ConfirmationController : ControllerBase
{
    private readonly IConfirmationStore _confirmationStore;
    private readonly ILogger<ConfirmationController> _logger;

    public ConfirmationController(
        IConfirmationStore confirmationStore,
        ILogger<ConfirmationController> logger)
    {
        _confirmationStore = confirmationStore;
        _logger = logger;
    }

    /// <summary>
    /// Approve a pending DML/DDL operation.
    /// The SSE controller will pick up this approval and execute the SQL.
    /// </summary>
    [HttpPost("{confirmId}")]
    public async Task<IActionResult> Approve(string confirmId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // Verify the pending confirmation exists and belongs to this user
        var pending = await _confirmationStore.GetAsync(confirmId);
        if (pending == null)
        {
            _logger.LogWarning("[Confirmation] Confirmation {ConfirmId} not found (expired?)", confirmId);
            return NotFound(new { error = "Confirmation not found or expired", confirmId });
        }

        if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
        {
            _logger.LogWarning("[Confirmation] User {UserId} attempted to confirm {ConfirmId} owned by {OwnerId}",
                userId, confirmId, pending.UserId);
            return Forbid();
        }

        // Record approval
        await _confirmationStore.SetResultAsync(confirmId, new ConfirmationResult
        {
            ConfirmId = confirmId,
            Action = ConfirmationAction.Approved,
            RespondedAt = DateTime.UtcNow
        });

        _logger.LogInformation("[Confirmation] ✅ User {UserId} APPROVED {ConfirmId}: {OpType} on {Table}",
            userId, confirmId, pending.OperationType, pending.TargetTable);

        return Ok(new
        {
            confirmId,
            action = "approved",
            message = "Operation approved. Execution will proceed shortly."
        });
    }

    /// <summary>
    /// Cancel a pending DML/DDL operation.
    /// The SSE controller will emit confirm_cancelled and close the stream.
    /// </summary>
    [HttpPost("{confirmId}/cancel")]
    public async Task<IActionResult> Cancel(string confirmId, [FromBody] CancelRequest? cancelRequest = null)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // Verify the pending confirmation exists and belongs to this user
        var pending = await _confirmationStore.GetAsync(confirmId);
        if (pending == null)
        {
            return NotFound(new { error = "Confirmation not found or expired", confirmId });
        }

        if (!string.Equals(pending.UserId, userId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        // Record cancellation
        await _confirmationStore.SetResultAsync(confirmId, new ConfirmationResult
        {
            ConfirmId = confirmId,
            Action = ConfirmationAction.Cancelled,
            CancelReason = cancelRequest?.Reason ?? "User cancelled",
            RespondedAt = DateTime.UtcNow
        });

        _logger.LogInformation("[Confirmation] ❌ User {UserId} CANCELLED {ConfirmId}: {Reason}",
            userId, confirmId, cancelRequest?.Reason ?? "no reason");

        return Ok(new
        {
            confirmId,
            action = "cancelled",
            message = "Operation cancelled."
        });
    }

    /// <summary>
    /// Check the status of a pending confirmation (optional — for frontend polling fallback).
    /// </summary>
    [HttpGet("{confirmId}/status")]
    public async Task<IActionResult> Status(string confirmId)
    {
        var pending = await _confirmationStore.GetAsync(confirmId);
        if (pending == null)
        {
            return NotFound(new { error = "Confirmation not found or expired" });
        }

        var result = await _confirmationStore.GetResultAsync(confirmId);

        return Ok(new
        {
            confirmId,
            status = result == null ? "pending" : result.Action.ToString().ToLowerInvariant(),
            operationType = pending.OperationType,
            targetTable = pending.TargetTable,
            riskLevel = pending.RiskLevel.ToString(),
            createdAt = pending.CreatedAt,
            timeoutSeconds = pending.TimeoutSeconds
        });
    }
}

public class CancelRequest
{
    public string? Reason { get; set; }
}
