namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Type of write operation
/// </summary>
public enum WriteOperationType
{
    Insert,
    Update,
    Delete,
    Upsert
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

    /// <summary>
    /// Pre-resolved entities from IntentClassifier to avoid duplicate LLM calls
    /// </summary>
    public List<string>? PreResolvedEntities { get; set; }

    /// <summary>
    /// ✅ OPTIMIZATION: Injected schema from controller to avoid Redis round-trip
    /// </summary>
    public DatabaseSchema? Schema { get; set; }
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

    /// <summary>Unique confirmation ID for SSE confirm flow (stored in Redis)</summary>
    public string? ConfirmId { get; set; }

    /// <summary>Risk level of this operation</summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Medium;

    /// <summary>Seconds before the confirmation times out (default 30s — enough for read + click)</summary>
    public int TimeoutSeconds { get; set; } = 30;
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
