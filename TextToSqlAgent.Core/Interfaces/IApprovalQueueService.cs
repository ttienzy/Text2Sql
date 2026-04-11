namespace TextToSqlAgent.Core.Interfaces;

/// <summary>
/// DTO for approval queue operations
/// </summary>
public class ApprovalQueueDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string SqlStatement { get; set; } = string.Empty;
    public int EstimatedRows { get; set; }
    public string RiskLevel { get; set; } = "MEDIUM";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime TimeoutAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ResponseAction { get; set; }
    public string? ModifiedSql { get; set; }
    public string? RejectionReason { get; set; }
    public string? ExecutionResult { get; set; }
    public int? AffectedRows { get; set; }
    public string? Warnings { get; set; }
    public bool HasWhereClause { get; set; }
}

/// <summary>
/// Service for managing async approval queue for Write/DDL operations
/// Eliminates blocking "Waiting for approval" UX
/// </summary>
public interface IApprovalQueueService
{
    /// <summary>
    /// Create a new approval request and store in database
    /// </summary>
    Task<ApprovalQueueDto> CreateApprovalAsync(ApprovalQueueDto approval, CancellationToken ct = default);

    /// <summary>
    /// Get pending approvals for a user
    /// </summary>
    Task<List<ApprovalQueueDto>> GetPendingApprovalsAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get approval by ID (with user validation)
    /// </summary>
    Task<ApprovalQueueDto?> GetApprovalByIdAsync(string approvalId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Approve an approval request
    /// </summary>
    Task<bool> ApproveAsync(string approvalId, string userId, string? modifiedSql = null, CancellationToken ct = default);

    /// <summary>
    /// Reject an approval request
    /// </summary>
    Task<bool> RejectAsync(string approvalId, string userId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Mark approval as executing
    /// </summary>
    Task<bool> MarkExecutingAsync(string approvalId, CancellationToken ct = default);

    /// <summary>
    /// Mark approval as completed with execution result
    /// </summary>
    Task<bool> MarkCompletedAsync(string approvalId, string executionResult, int affectedRows, CancellationToken ct = default);

    /// <summary>
    /// Mark approval as timed out
    /// </summary>
    Task<int> TimeoutExpiredApprovalsAsync(CancellationToken ct = default);
}
