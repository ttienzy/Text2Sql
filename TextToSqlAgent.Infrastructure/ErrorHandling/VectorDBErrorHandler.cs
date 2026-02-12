using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.ErrorHandling;

/// <summary>
/// Handles Vector Database (Qdrant) errors
/// </summary>
public class VectorDBErrorHandler : BaseErrorHandler
{
    public VectorDBErrorHandler(ILogger<VectorDBErrorHandler> logger) 
        : base(logger)
    {
    }

    /// <summary>
    /// Handle Vector DB operation errors
    /// </summary>
    public async Task<T> HandleVectorDBErrorAsync<T>(
        Func<Task<T>> operation,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        Logger.LogWarning(
            exception,
            "[VectorDB Handler] Vector DB error: {Message}",
            exception.Message);

        // Create error object
        var error = AnalyzeVectorDBError(exception);

        // Handle based on error type
        return error.Type switch
        {
            SqlErrorType.VectorDBUnavailable => 
                await HandleUnavailableAsync(operation, error, cancellationToken),
            
            SqlErrorType.SchemaNotIndexed => 
                await HandleNotIndexedAsync(operation, error, cancellationToken),
            
            SqlErrorType.EmbeddingGenerationFailed => 
                await HandleEmbeddingFailedAsync(operation, error, cancellationToken),
            
            _ => await HandleAsync(operation, error, cancellationToken)
        };
    }

    private async Task<T> HandleUnavailableAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning("[VectorDB Handler] Vector DB unavailable. Retrying with backoff...");
        
        // Use exponential backoff for service unavailability
        error.RecommendedStrategy = RetryStrategy.ExponentialBackoff;
        error.MaxRetryAttempts = 3;
        error.IsRecoverable = true;

        try
        {
            return await HandleAsync(operation, error, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[VectorDB Handler] Vector DB still unavailable after retries");
            
            throw new VectorDBException(
                "Vector database is unavailable. Schema retrieval disabled. " +
                "Falling back to full schema scan.",
                ex);
        }
    }

    private async Task<T> HandleNotIndexedAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning(
            "[VectorDB Handler] Schema not indexed. This should trigger indexing.");

        // Schema not indexed is not retryable - caller should index first
        throw new SchemaException(
            "Database schema has not been indexed. Please run schema indexing first.");
    }

    private async Task<T> HandleEmbeddingFailedAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning("[VectorDB Handler] Embedding generation failed. Retrying...");
        
        // Retry embedding generation with wait
        error.RecommendedStrategy = RetryStrategy.WaitAndRetry;
        error.MaxRetryAttempts = 2;
        error.IsRecoverable = true;

        try
        {
            return await HandleAsync(operation, error, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[VectorDB Handler] Embedding generation failed after retries");
            
            throw new VectorDBException(
                "Failed to generate embeddings. Falling back to keyword search.",
                ex);
        }
    }

    private SqlError AnalyzeVectorDBError(Exception exception)
    {
        var message = exception.Message.ToLower();
        
        // Analyze error type based on message
        if (message.Contains("connection") || message.Contains("unavailable") || 
            message.Contains("timeout") || message.Contains("refused"))
        {
            return new SqlError
            {
                Type = SqlErrorType.VectorDBUnavailable,
                Category = ErrorCategory.VectorDB,
                ErrorMessage = exception.Message,
                Severity = ErrorSeverity.High,
                IsRecoverable = true,
                RecommendedStrategy = RetryStrategy.ExponentialBackoff,
                MaxRetryAttempts = 3,
                ErrorCode = "VDB_UNAVAIL_001",
                SuggestedFix = "Vector DB không khả dụng. Kiểm tra kết nối Qdrant."
            };
        }

        if (message.Contains("not found") || message.Contains("collection") || 
            message.Contains("not indexed"))
        {
            return new SqlError
            {
                Type = SqlErrorType.SchemaNotIndexed,
                Category = ErrorCategory.VectorDB,
                ErrorMessage = exception.Message,
                Severity = ErrorSeverity.Medium,
                IsRecoverable = false,
                RecommendedStrategy = RetryStrategy.NoRetry,
                ErrorCode = "VDB_INDEX_001",
                SuggestedFix = "Schema chưa được index. Chạy schema indexing trước."
            };
        }

        if (message.Contains("embedding") || message.Contains("api") || 
            message.Contains("gemini"))
        {
            return new SqlError
            {
                Type = SqlErrorType.EmbeddingGenerationFailed,
                Category = ErrorCategory.VectorDB,
                ErrorMessage = exception.Message,
                Severity = ErrorSeverity.Medium,
                IsRecoverable = true,
                RecommendedStrategy = RetryStrategy.WaitAndRetry,
                MaxRetryAttempts = 2,
                ErrorCode = "VDB_EMBED_001",
                SuggestedFix = "Không thể tạo embedding. Kiểm tra Gemini API."
            };
        }

        // Unknown vector DB error
        return new SqlError
        {
            Type = SqlErrorType.Unknown,
            Category = ErrorCategory.VectorDB,
            ErrorMessage = exception.Message,
            Severity = ErrorSeverity.Medium,
            IsRecoverable = false,
            RecommendedStrategy = RetryStrategy.NoRetry,
            ErrorCode = "VDB_ERR_001",
            SuggestedFix = "Lỗi VectorDB không xác định. Xem log chi tiết."
        };
    }

    protected override Exception CreateException(SqlError error)
    {
        return error.Type switch
        {
            SqlErrorType.VectorDBUnavailable => new VectorDBException(error.ErrorMessage),
            SqlErrorType.SchemaNotIndexed => new SchemaException(error.ErrorMessage),
            SqlErrorType.EmbeddingGenerationFailed => new VectorDBException(error.ErrorMessage),
            _ => base.CreateException(error)
        };
    }
}
