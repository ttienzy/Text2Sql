namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Represents a pending DML/DDL operation stored in Redis awaiting user confirmation.
/// Key pattern: pending:confirm:{ConfirmId}
/// TTL: TimeoutSeconds (default 60s)
/// </summary>
public class PendingConfirmation
{
    /// <summary>Unique confirmation ID (UUID)</summary>
    public string ConfirmId { get; set; } = string.Empty;

    /// <summary>Generated SQL to execute upon confirmation</summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>Type of DML/DDL operation</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>Primary target table/object</summary>
    public string TargetTable { get; set; } = string.Empty;

    /// <summary>Risk assessment</summary>
    public RiskLevel RiskLevel { get; set; }

    /// <summary>Estimated number of rows affected</summary>
    public int EstimatedRows { get; set; }

    /// <summary>When the confirmation was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Seconds before timeout (default 60)</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Database connection ID</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>User who initiated the operation</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Conversation ID for context persistence</summary>
    public string? ConversationId { get; set; }

    /// <summary>Original user question</summary>
    public string OriginalQuestion { get; set; } = string.Empty;

    /// <summary>Warnings associated with this operation</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Columns affected by the operation</summary>
    public List<string> AffectedColumns { get; set; } = new();

    /// <summary>Whether the operation has a WHERE clause</summary>
    public bool HasWhereClause { get; set; }
}

/// <summary>
/// Result of a confirmation action (approve/cancel)
/// </summary>
public enum ConfirmationAction
{
    /// <summary>User approved the operation</summary>
    Approved,

    /// <summary>User explicitly cancelled</summary>
    Cancelled,

    /// <summary>Confirmation timed out (60s)</summary>
    TimedOut
}

/// <summary>
/// Stored alongside PendingConfirmation in Redis when user responds.
/// The SSE polling loop reads this to know whether to proceed.
/// Key pattern: pending:confirm:{ConfirmId}:result
/// </summary>
public class ConfirmationResult
{
    public string ConfirmId { get; set; } = string.Empty;
    public ConfirmationAction Action { get; set; }
    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
    public string? CancelReason { get; set; }
}
