using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Exceptions;

// Base exception
public class AgentException : Exception
{
    public string ErrorCode { get; set; } = string.Empty;
    public ErrorSeverity Severity { get; set; }

    public AgentException(string message, string errorCode = "", ErrorSeverity severity = ErrorSeverity.Medium)
        : base(message)
    {
        ErrorCode = errorCode;
        Severity = severity;
    }

    public AgentException(string message, Exception innerException, string errorCode = "", ErrorSeverity severity = ErrorSeverity.Medium)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Severity = severity;
    }
}

// Database exceptions
public class DatabaseConnectionException : AgentException
{
    public DatabaseConnectionException(string message, Exception? innerException = null)
        : base(message, innerException, "DB_CONN_001", ErrorSeverity.High) { }
}

public class DatabaseTimeoutException : AgentException
{
    public DatabaseTimeoutException(string message, Exception? innerException = null)
        : base(message, innerException, "DB_TIMEOUT_001", ErrorSeverity.Medium) { }
}

public class DatabasePermissionException : AgentException
{
    public DatabasePermissionException(string message, Exception? innerException = null)
        : base(message, innerException, "DB_PERM_001", ErrorSeverity.High) { }
}

// LLM exceptions
public class LLMApiException : AgentException
{
    public int? HttpStatusCode { get; set; }

    public LLMApiException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException, "LLM_API_001", ErrorSeverity.Medium)
    {
        HttpStatusCode = statusCode;
    }
}

public class RateLimitException : LLMApiException
{
    public int? RetryAfterSeconds { get; set; }

    public RateLimitException(string message, int? retryAfter = null)
        : base(message, 429)
    {
        ErrorCode = "LLM_RATE_001";
        RetryAfterSeconds = retryAfter;
    }
}

public class QuotaExceededException : LLMApiException
{
    public QuotaExceededException(string message)
        : base(message, 403)
    {
        ErrorCode = "LLM_QUOTA_001";
        Severity = ErrorSeverity.Critical;
    }
}

// Schema exceptions
public class SchemaException : AgentException
{
    public SchemaException(string message, Exception? innerException = null)
        : base(message, innerException, "SCHEMA_001", ErrorSeverity.Medium) { }
}

public class VectorDBException : AgentException
{
    public VectorDBException(string message, Exception? innerException = null)
        : base(message, innerException, "VDB_001", ErrorSeverity.High) { }
}