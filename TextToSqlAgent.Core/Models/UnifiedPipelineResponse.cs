namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Unified response envelope for all pipeline types
/// Provides consistent structure across QUERY, WRITE, DDL, FORBIDDEN, and REJECT pipelines
/// </summary>
public class UnifiedPipelineResponse
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Response schema version for future compatibility
    /// </summary>
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Pipeline that processed this request
    /// </summary>
    public PipelineType Pipeline { get; set; }

    /// <summary>
    /// Timestamp when the response was generated
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Intent classification summary (filtered for client consumption)
    /// </summary>
    public IntentSummary Intent { get; set; } = null!;

    /// <summary>
    /// User-facing message describing the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Pipeline-specific data (strongly typed via marker interface)
    /// </summary>
    public IPipelineData Data { get; set; } = null!;

    /// <summary>
    /// Generated SQL statement (convenience field for UI)
    /// Populated for QUERY, WRITE, DDL pipelines; null for FORBIDDEN/REJECT
    /// </summary>
    public string? SqlGenerated { get; set; }

    /// <summary>
    /// Whether this operation requires user confirmation before execution
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// Warnings about potential issues or risks
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Suggestions for follow-up actions or improvements
    /// </summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>
    /// Error details if operation failed
    /// </summary>
    public ErrorDetails? Error { get; set; }

    /// <summary>
    /// Execution metadata for observability and debugging
    /// </summary>
    public ExecutionMetadata Execution { get; set; } = new();
}

/// <summary>
/// Pipeline type enumeration
/// </summary>
public enum PipelineType
{
    Query,
    Write,
    Ddl,
    Forbidden,
    Reject
}

/// <summary>
/// Filtered intent summary for client consumption
/// Does not expose internal reasoning or normalized queries
/// </summary>
public class IntentSummary
{
    /// <summary>
    /// Detected intent category
    /// </summary>
    public IntentCategory Type { get; set; }

    /// <summary>
    /// Pipeline route decision
    /// </summary>
    public PipelineRoute Route { get; set; }

    /// <summary>
    /// Classification confidence score (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Entities detected in the query (table names, column names)
    /// </summary>
    public List<string> DetectedEntities { get; set; } = new();

    /// <summary>
    /// Keywords that triggered the classification
    /// </summary>
    public List<string> MatchedKeywords { get; set; } = new();
}

/// <summary>
/// Error details for failed operations
/// </summary>
public class ErrorDetails
{
    /// <summary>
    /// Error code for programmatic handling
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Stack trace for debugging (only in development)
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Additional context information
    /// </summary>
    public Dictionary<string, object>? AdditionalInfo { get; set; }
}

/// <summary>
/// Execution metadata for observability
/// </summary>
public class ExecutionMetadata
{
    /// <summary>
    /// Total execution duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Total tokens consumed by LLM calls
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// Number of LLM API calls made
    /// </summary>
    public int LlmCalls { get; set; }

    /// <summary>
    /// Whether result was served from cache
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// Processing steps for debugging
    /// </summary>
    public List<string> ProcessingSteps { get; set; } = new();

    /// <summary>
    /// Number of correction attempts (for query pipeline)
    /// </summary>
    public int CorrectionAttempts { get; set; }

    /// <summary>
    /// Whether SQL was auto-corrected
    /// </summary>
    public bool WasCorrected { get; set; }
}
