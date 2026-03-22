namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Type of write operation
/// </summary>
public enum WriteOperationType
{
    Insert,
    Update
}

/// <summary>
/// Request for write operation
/// </summary>
public class WriteOperationRequest
{
    public string Question { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public bool IsConfirmed { get; set; } = false;
}

/// <summary>
/// Preview of write operation before execution
/// </summary>
public class WriteOperationPreview
{
    public string SqlStatement { get; set; } = string.Empty;
    public WriteOperationType OperationType { get; set; }
    public string TargetTable { get; set; } = string.Empty;
    public int EstimatedAffectedRows { get; set; }
    public List<string> Warnings { get; set; } = new();
    public bool RequiresConfirmation { get; set; } = true;
    public bool HasWhereClause { get; set; }
    public List<string> AffectedColumns { get; set; } = new();
    public string? ValidationError { get; set; }
}

/// <summary>
/// Result of write operation execution
/// </summary>
public class WriteOperationResult
{
    public bool Success { get; set; }
    public int ActualAffectedRows { get; set; }
    public string SqlExecuted { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public WriteOperationType OperationType { get; set; }
    public string TargetTable { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public List<string> ProcessingSteps { get; set; } = new();
}
