using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Exceptions;

namespace TextToSqlAgent.Infrastructure.ErrorHandling;

/// <summary>
/// Base class for all error handlers
/// Provides common retry logic and error recovery mechanisms
/// </summary>
public abstract class BaseErrorHandler
{
    protected readonly ILogger Logger;

    protected BaseErrorHandler(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// Handle error with automatic retry based on error type
    /// </summary>
    public async Task<T> HandleAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug(
            "[{Handler}] Handling error: {ErrorType} - {Message}",
            GetType().Name,
            error.Type,
            error.ErrorMessage);

        // Check if error is recoverable
        if (!error.IsRecoverable)
        {
            Logger.LogError(
                "[{Handler}] Error is not recoverable: {ErrorCode}",
                GetType().Name,
                error.ErrorCode);
            
            throw CreateException(error);
        }

        // Apply retry strategy
        return await ApplyRetryStrategyAsync(operation, error, cancellationToken);
    }

    /// <summary>
    /// Apply retry strategy based on error configuration
    /// </summary>
    protected async Task<T> ApplyRetryStrategyAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        switch (error.RecommendedStrategy)
        {
            case RetryStrategy.NoRetry:
                Logger.LogWarning("[{Handler}] No retry recommended", GetType().Name);
                throw CreateException(error);

            case RetryStrategy.ImmediateRetry:
                return await RetryImmediateAsync(operation, error.MaxRetryAttempts, cancellationToken);

            case RetryStrategy.ExponentialBackoff:
                return await RetryWithExponentialBackoffAsync(operation, error.MaxRetryAttempts, cancellationToken);

            case RetryStrategy.WaitAndRetry:
                return await RetryWithWaitAsync(operation, error.MaxRetryAttempts, cancellationToken);

            case RetryStrategy.CircuitBreaker:
                return await RetryWithCircuitBreakerAsync(operation, error.MaxRetryAttempts, cancellationToken);

            case RetryStrategy.Fallback:
                return await RetryWithFallbackAsync(operation, error, cancellationToken);

            default:
                throw CreateException(error);
        }
    }

    /// <summary>
    /// Immediate retry without delay
    /// </summary>
    protected async Task<T> RetryImmediateAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                attempt++;
                Logger.LogDebug(
                    "[{Handler}] Immediate retry attempt {Attempt}/{Max}",
                    GetType().Name,
                    attempt,
                    maxRetries);

                return await operation();
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logger.LogWarning(
                    ex,
                    "[{Handler}] Retry {Attempt} failed",
                    GetType().Name,
                    attempt);

                if (attempt >= maxRetries)
                    break;
            }
        }

        Logger.LogError("[{Handler}] All retries exhausted", GetType().Name);
        throw lastException!;
    }

    /// <summary>
    /// Retry with exponential backoff (2^attempt seconds)
    /// </summary>
    protected async Task<T> RetryWithExponentialBackoffAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                attempt++;
                Logger.LogDebug(
                    "[{Handler}] Exponential backoff retry attempt {Attempt}/{Max}",
                    GetType().Name,
                    attempt,
                    maxRetries);

                return await operation();
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                if (attempt >= maxRetries)
                    break;

                var delaySeconds = Math.Pow(2, attempt);
                Logger.LogWarning(
                    ex,
                    "[{Handler}] Retry {Attempt} failed. Waiting {Delay}s...",
                    GetType().Name,
                    attempt,
                    delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        Logger.LogError("[{Handler}] All retries exhausted", GetType().Name);
        throw lastException!;
    }

    /// <summary>
    /// Retry with fixed wait time (5 seconds)
    /// </summary>
    protected virtual async Task<T> RetryWithWaitAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;
        var waitSeconds = 5;

        while (attempt < maxRetries)
        {
            try
            {
                attempt++;
                Logger.LogDebug(
                    "[{Handler}] Wait and retry attempt {Attempt}/{Max}",
                    GetType().Name,
                    attempt,
                    maxRetries);

                return await operation();
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                if (attempt >= maxRetries)
                    break;

                Logger.LogWarning(
                    ex,
                    "[{Handler}] Retry {Attempt} failed. Waiting {Delay}s...",
                    GetType().Name,
                    attempt,
                    waitSeconds);

                await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
            }
        }

        Logger.LogError("[{Handler}] All retries exhausted", GetType().Name);
        throw lastException!;
    }

    /// <summary>
    /// Circuit breaker pattern - fail fast after threshold
    /// </summary>
    protected virtual async Task<T> RetryWithCircuitBreakerAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        // Simplified circuit breaker - can be enhanced with Polly
        return await RetryWithExponentialBackoffAsync(operation, maxRetries, cancellationToken);
    }

    /// <summary>
    /// Retry with fallback - subclasses should override
    /// </summary>
    protected virtual async Task<T> RetryWithFallbackAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning("[{Handler}] No fallback implemented", GetType().Name);
        throw CreateException(error);
    }

    /// <summary>
    /// Create appropriate exception based on error
    /// </summary>
    protected virtual Exception CreateException(SqlError error)
    {
        return new AgentException(
            error.ErrorMessage,
            error.ErrorCode ?? "UNKNOWN",
            error.Severity);
    }

    /// <summary>
    /// Check if error should be retried
    /// </summary>
    protected virtual bool ShouldRetry(SqlError error)
    {
        return error.IsRecoverable && 
               error.RecommendedStrategy != RetryStrategy.NoRetry;
    }
}
