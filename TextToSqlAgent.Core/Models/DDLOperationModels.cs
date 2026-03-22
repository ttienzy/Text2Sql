namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Type of DDL operation
/// </summary>
public enum DDLOperationType
{
    CreateIndex,
    DropIndex,
    CreateProcedure,
    AlterProcedure,
    CreateFunction,
    AlterFunction,
    CreateView,
    AlterView,
    AlterTableAddColumn,
    AlterTableModifyColumn,
    AlterTableDropColumn,
    Unknown
}

/// <summary>
/// Request for DDL operation
/// </summary>
public class DDLOperationRequest
{
    public string Question { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public bool IsConfirmed { get; set; } = false;
}

/// <summary>
/// Impact analysis for DDL operation
/// </summary>
public class DDLImpactAnalysis
{
    /// <summary>Estimated storage size in bytes</summary>
    public long EstimatedStorageBytes { get; set; }

    /// <summary>Estimated lock duration</summary>
    public TimeSpan EstimatedLockDuration { get; set; }

    /// <summary>Estimated performance gain multiplier (e.g., 40x faster)</summary>
    public double EstimatedPerformanceGain { get; set; }

    /// <summary>Write overhead percentage for indexes</summary>
    public double WriteOverheadPercent { get; set; }

    /// <summary>Objects that will be affected</summary>
    public List<string> AffectedObjects { get; set; } = new();

    /// <summary>Warnings about potential issues</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Benefits of this DDL operation</summary>
    public List<string> Benefits { get; set; } = new();
}

/// <summary>
/// Preview of DDL operation before execution
/// </summary>
public class DDLOperationPreview
{
    public string DDLScript { get; set; } = string.Empty;
    public DDLOperationType OperationType { get; set; }
    public string TargetObject { get; set; } = string.Empty;
    public DDLImpactAnalysis Impact { get; set; } = new();
    public bool RequiresConfirmation { get; set; } = true;
    public string? ValidationError { get; set; }
    public List<string> RelatedObjects { get; set; } = new();
}

/// <summary>
/// Result of DDL operation execution
/// </summary>
public class DDLOperationResult
{
    public bool Success { get; set; }
    public string DDLExecuted { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
    public string? ErrorMessage { get; set; }
    public DDLOperationType OperationType { get; set; }
    public string TargetObject { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public List<string> ProcessingSteps { get; set; } = new();
    public bool SchemaCacheReloaded { get; set; }
}
