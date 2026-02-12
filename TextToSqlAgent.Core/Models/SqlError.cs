namespace TextToSqlAgent.Core.Models;

public class SqlError
{
    public SqlErrorType Type { get; set; }
    public ErrorSeverity Severity { get; set; }
    public ErrorCategory Category { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? InvalidElement { get; set; }
    public string? SuggestedFix { get; set; }
    public bool IsRecoverable { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Recovery info
    public int MaxRetryAttempts { get; set; } = 3;
    public RetryStrategy RecommendedStrategy { get; set; }
}

public enum SqlErrorType
{
    Unknown,

    // SQL Errors (existing)
    InvalidColumnName,
    InvalidTableName,
    SyntaxError,
    InvalidObjectName,
    AmbiguousColumnName,
    TypeMismatch,
    ParameterNotDeclared,

    // Connection Errors (NEW)
    ConnectionTimeout,
    ConnectionFailed,
    ConnectionRefused,
    NetworkError,

    // Permission Errors (NEW)
    PermissionDenied,
    InsufficientPrivileges,
    DatabaseAccessDenied,

    // Execution Errors (NEW)
    QueryTimeout,
    DeadlockDetected,
    TransactionRollback,

    // LLM API Errors (NEW)
    LLMRateLimitExceeded,
    LLMQuotaExceeded,
    LLMInvalidApiKey,
    LLMServiceUnavailable,
    LLMTimeout,
    LLMBadRequest,

    // Schema Errors (NEW)
    SchemaNotIndexed,
    VectorDBUnavailable,
    EmbeddingGenerationFailed
}

public enum ErrorSeverity
{
    Low,        // Warning, không ảnh hưởng workflow
    Medium,     // Có thể retry hoặc workaround
    High,       // Blocking, cần fix
    Critical    // System failure
}

public enum ErrorCategory
{
    Database,
    LLM,
    Network,
    Configuration,
    UserInput,
    Internal,
    VectorDB
}

public enum RetryStrategy
{
    NoRetry,
    ImmediateRetry,
    ExponentialBackoff,
    WaitAndRetry,
    CircuitBreaker,
    Fallback
}