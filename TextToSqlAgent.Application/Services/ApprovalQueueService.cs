using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Service for managing async approval queue for Write/DDL operations
/// Implements the async queue pattern to eliminate blocking UX
/// </summary>
public class ApprovalQueueService : IApprovalQueueService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ApprovalQueueService> _logger;

    public ApprovalQueueService(
        AppDbContext dbContext,
        ILogger<ApprovalQueueService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ApprovalQueueDto> CreateApprovalAsync(ApprovalQueueDto approvalDto, CancellationToken ct = default)
    {
        var approval = MapToEntity(approvalDto);
        _dbContext.ApprovalQueues.Add(approval);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ApprovalQueue] Created approval {Id} for user {UserId} - {Type} on {Table}",
            approval.Id, approval.UserId, approval.OperationType, approval.TargetTable);

        return MapToDto(approval);
    }

    public async Task<List<ApprovalQueueDto>> GetPendingApprovalsAsync(string userId, CancellationToken ct = default)
    {
        var approvals = await _dbContext.ApprovalQueues
            .Where(a => a.UserId == userId && a.Status == "pending")
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return approvals.Select(MapToDto).ToList();
    }

    public async Task<ApprovalQueueDto?> GetApprovalByIdAsync(string approvalId, string userId, CancellationToken ct = default)
    {
        var approval = await _dbContext.ApprovalQueues
            .FirstOrDefaultAsync(a => a.Id == approvalId && a.UserId == userId, ct);

        return approval != null ? MapToDto(approval) : null;
    }

    public async Task<bool> ApproveAsync(string approvalId, string userId, string? modifiedSql = null, CancellationToken ct = default)
    {
        var approval = await _dbContext.ApprovalQueues
            .FirstOrDefaultAsync(a => a.Id == approvalId && a.UserId == userId, ct);

        if (approval == null || approval.Status != "pending")
        {
            _logger.LogWarning(
                "[ApprovalQueue] Cannot approve {Id} - not found or not pending (status: {Status})",
                approvalId, approval?.Status ?? "null");
            return false;
        }

        approval.Status = "approved";
        approval.ResponseAction = "approve";
        approval.RespondedAt = DateTime.UtcNow;
        approval.ModifiedSql = modifiedSql;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ApprovalQueue] Approved {Id} by user {UserId} - Modified: {Modified}",
            approvalId, userId, modifiedSql != null);

        return true;
    }

    public async Task<bool> RejectAsync(string approvalId, string userId, string reason, CancellationToken ct = default)
    {
        var approval = await _dbContext.ApprovalQueues
            .FirstOrDefaultAsync(a => a.Id == approvalId && a.UserId == userId, ct);

        if (approval == null || approval.Status != "pending")
        {
            _logger.LogWarning(
                "[ApprovalQueue] Cannot reject {Id} - not found or not pending",
                approvalId);
            return false;
        }

        approval.Status = "rejected";
        approval.ResponseAction = "reject";
        approval.RespondedAt = DateTime.UtcNow;
        approval.RejectionReason = reason;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ApprovalQueue] Rejected {Id} by user {UserId} - Reason: {Reason}",
            approvalId, userId, reason);

        return true;
    }

    public async Task<bool> MarkExecutingAsync(string approvalId, CancellationToken ct = default)
    {
        var approval = await _dbContext.ApprovalQueues
            .FirstOrDefaultAsync(a => a.Id == approvalId, ct);

        if (approval == null || approval.Status != "approved")
        {
            return false;
        }

        approval.Status = "executing";
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("[ApprovalQueue] Executing {Id}", approvalId);
        return true;
    }

    public async Task<bool> MarkCompletedAsync(string approvalId, string executionResult, int affectedRows, CancellationToken ct = default)
    {
        var approval = await _dbContext.ApprovalQueues
            .FirstOrDefaultAsync(a => a.Id == approvalId, ct);

        if (approval == null)
        {
            return false;
        }

        approval.Status = "completed";
        approval.ExecutedAt = DateTime.UtcNow;
        approval.ExecutionResult = executionResult;
        approval.AffectedRows = affectedRows;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ApprovalQueue] Completed {Id} - Affected {Rows} rows",
            approvalId, affectedRows);

        return true;
    }

    public async Task<int> TimeoutExpiredApprovalsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiredApprovals = await _dbContext.ApprovalQueues
            .Where(a => a.Status == "pending" && a.TimeoutAt < now)
            .ToListAsync(ct);

        foreach (var approval in expiredApprovals)
        {
            approval.Status = "timeout";
            _logger.LogWarning(
                "[ApprovalQueue] Timed out {Id} - Created: {Created}, Timeout: {Timeout}",
                approval.Id, approval.CreatedAt, approval.TimeoutAt);
        }

        if (expiredApprovals.Any())
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        return expiredApprovals.Count;
    }

    private static ApprovalQueue MapToEntity(ApprovalQueueDto dto)
    {
        return new ApprovalQueue
        {
            Id = dto.Id,
            UserId = dto.UserId,
            ConnectionId = dto.ConnectionId,
            ConversationId = dto.ConversationId,
            Question = dto.Question,
            OperationType = dto.OperationType,
            TargetTable = dto.TargetTable,
            SqlStatement = dto.SqlStatement,
            EstimatedRows = dto.EstimatedRows,
            RiskLevel = dto.RiskLevel,
            Status = dto.Status,
            CreatedAt = dto.CreatedAt,
            TimeoutAt = dto.TimeoutAt,
            RespondedAt = dto.RespondedAt,
            ExecutedAt = dto.ExecutedAt,
            ResponseAction = dto.ResponseAction,
            ModifiedSql = dto.ModifiedSql,
            RejectionReason = dto.RejectionReason,
            ExecutionResult = dto.ExecutionResult,
            AffectedRows = dto.AffectedRows,
            Warnings = dto.Warnings,
            HasWhereClause = dto.HasWhereClause
        };
    }

    private static ApprovalQueueDto MapToDto(ApprovalQueue entity)
    {
        return new ApprovalQueueDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            ConnectionId = entity.ConnectionId,
            ConversationId = entity.ConversationId,
            Question = entity.Question,
            OperationType = entity.OperationType,
            TargetTable = entity.TargetTable,
            SqlStatement = entity.SqlStatement,
            EstimatedRows = entity.EstimatedRows,
            RiskLevel = entity.RiskLevel,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            TimeoutAt = entity.TimeoutAt,
            RespondedAt = entity.RespondedAt,
            ExecutedAt = entity.ExecutedAt,
            ResponseAction = entity.ResponseAction,
            ModifiedSql = entity.ModifiedSql,
            RejectionReason = entity.RejectionReason,
            ExecutionResult = entity.ExecutionResult,
            AffectedRows = entity.AffectedRows,
            Warnings = entity.Warnings,
            HasWhereClause = entity.HasWhereClause
        };
    }
}
