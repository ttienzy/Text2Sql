namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Async approval queue for Write/DDL operations
/// Allows users to review and approve SQL operations without blocking
/// </summary>
public class ApprovalQueue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }

    // Request info
    public string Question { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty; // 'INSERT', 'UPDATE', 'DELETE', 'DDL'
    public string TargetTable { get; set; } = string.Empty;

    // Generated SQL
    public string SqlStatement { get; set; } = string.Empty;
    public int EstimatedRows { get; set; }
    public string RiskLevel { get; set; } = "MEDIUM"; // 'LOW', 'MEDIUM', 'HIGH'

    // Status
    public string Status { get; set; } = "pending"; // 'pending', 'approved', 'rejected', 'timeout', 'executing', 'completed'

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime TimeoutAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }

    // Response
    public string? ResponseAction { get; set; } // 'approve', 'modify', 'reject'
    public string? ModifiedSql { get; set; } // If user modified
    public string? RejectionReason { get; set; }

    // Result
    public string? ExecutionResult { get; set; } // JSON
    public int? AffectedRows { get; set; }

    // Additional metadata
    public string? Warnings { get; set; } // JSON array
    public bool HasWhereClause { get; set; }
}
