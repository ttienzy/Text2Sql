using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.ErrorHandling;

/// <summary>
/// Handles LLM API errors (rate limits, quotas, timeouts)
/// </summary>
public class LLMErrorHandler : BaseErrorHandler
{
    private readonly SqlErrorAnalyzer _errorAnalyzer;
    private DateTime? _rateLimitResetTime;
    private int _rateLimitRetryAfterSeconds = 60;

    public LLMErrorHandler(
        ILogger<LLMErrorHandler> logger,
        SqlErrorAnalyzer errorAnalyzer) 
        : base(logger)
    {
        _errorAnalyzer = errorAnalyzer;
    }

    /// <summary>
    /// Handle LLM API errors with rate limit awareness
    /// </summary>
    public async Task<T> HandleLLMErrorAsync<T>(
        Func<Task<T>> operation,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        // Check if we're still in rate limit cooldown
        if (IsInRateLimitCooldown())
        {
            var waitTime = (_rateLimitResetTime!.Value - DateTime.UtcNow).TotalSeconds;
            
            Logger.LogWarning(
                "[LLM Handler] Still in rate limit cooldown. Wait {Seconds}s more",
                waitTime);

            throw new RateLimitException(
                $"Rate limit active. Retry after {waitTime:F0} seconds",
                (int)waitTime);
        }

        // Analyze the LLM error
        var sqlError = _errorAnalyzer.AnalyzeLLMError(exception);

        // Handle rate limit specially
        if (sqlError.Type == SqlErrorType.LLMRateLimitExceeded)
        {
            return await HandleRateLimitAsync(operation, sqlError, cancellationToken);
        }

        // Handle quota exceeded - no retry
        if (sqlError.Type == SqlErrorType.LLMQuotaExceeded)
        {
            Logger.LogError("[LLM Handler] Quota exceeded - cannot retry");
            throw new QuotaExceededException(sqlError.ErrorMessage);
        }

        // Handle invalid API key - no retry
        if (sqlError.Type == SqlErrorType.LLMInvalidApiKey)
        {
            Logger.LogError("[LLM Handler] Invalid API key - cannot retry");
            throw new LLMApiException(sqlError.ErrorMessage, 401, exception);
        }

        // Other errors - use standard retry
        return await HandleAsync(operation, sqlError, cancellationToken);
    }

    /// <summary>
    /// Handle rate limit with smart waiting
    /// </summary>
    private async Task<T> HandleRateLimitAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        // Set rate limit reset time
        _rateLimitResetTime = DateTime.UtcNow.AddSeconds(_rateLimitRetryAfterSeconds);

        Logger.LogWarning(
            "[LLM Handler] Rate limit hit. Waiting {Seconds}s until {ResetTime}",
            _rateLimitRetryAfterSeconds,
            _rateLimitResetTime);

        // Wait for rate limit to reset
        await Task.Delay(
            TimeSpan.FromSeconds(_rateLimitRetryAfterSeconds), 
            cancellationToken);

        // Try again after waiting
        try
        {
            Logger.LogInformation("[LLM Handler] Retrying after rate limit wait");
            var result = await operation();
            
            // Success - clear rate limit
            ClearRateLimit();
            
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LLM Handler] Retry failed after rate limit wait");
            throw new RateLimitException(
                "Rate limit retry failed. Please try again later.",
                _rateLimitRetryAfterSeconds);
        }
    }

    protected override async Task<T> RetryWithWaitAsync<T>(
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
                
                // Progressive wait: 5s, 10s, 15s...
                var waitSeconds = attempt * 5;
                
                Logger.LogInformation(
                    "[LLM Handler] Service retry {Attempt}/{Max} after {Delay}s",
                    attempt,
                    maxRetries,
                    waitSeconds);

                if (attempt > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
                }

                return await operation();
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logger.LogWarning(
                    ex,
                    "[LLM Handler] Retry {Attempt} failed",
                    attempt);

                if (attempt >= maxRetries)
                    break;
            }
        }

        Logger.LogError("[LLM Handler] All retries exhausted");
        throw lastException!;
    }

    protected override async Task<T> RetryWithFallbackAsync<T>(
        Func<Task<T>> operation,
        SqlError error,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LLM Handler] Attempting fallback strategy");

        try
        {
            // Try with exponential backoff first
            return await RetryWithExponentialBackoffAsync(
                operation, 
                error.MaxRetryAttempts, 
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LLM Handler] Fallback strategy failed");
            
            // Could implement alternative LLM model here
            // For now, just throw
            throw new LLMApiException(
                "LLM request failed after fallback attempts",
                null,
                ex);
        }
    }

    private bool IsInRateLimitCooldown()
    {
        if (_rateLimitResetTime == null)
            return false;

        if (DateTime.UtcNow < _rateLimitResetTime.Value)
            return true;

        // Cooldown expired
        ClearRateLimit();
        return false;
    }

    private void ClearRateLimit()
    {
        if (_rateLimitResetTime != null)
        {
            Logger.LogInformation("[LLM Handler] Rate limit cleared");
        }
        _rateLimitResetTime = null;
    }

    protected override Exception CreateException(SqlError error)
    {
        return error.Type switch
        {
            SqlErrorType.LLMRateLimitExceeded => new RateLimitException(
                error.ErrorMessage, 
                _rateLimitRetryAfterSeconds),
            
            SqlErrorType.LLMQuotaExceeded => new QuotaExceededException(error.ErrorMessage),
            
            SqlErrorType.LLMInvalidApiKey => new LLMApiException(
                error.ErrorMessage, 
                401),
            
            SqlErrorType.LLMServiceUnavailable => new LLMApiException(
                error.ErrorMessage, 
                503),
            
            SqlErrorType.LLMTimeout => new LLMApiException(
                error.ErrorMessage, 
                408),
            
            _ => new LLMApiException(error.ErrorMessage)
        };
    }

    /// <summary>
    /// Get current rate limit status
    /// </summary>
    public string GetRateLimitStatus()
    {
        if (IsInRateLimitCooldown())
        {
            var remaining = (_rateLimitResetTime!.Value - DateTime.UtcNow).TotalSeconds;
            return $"ACTIVE (resets in {remaining:F0}s)";
        }

        return "NONE";
    }

    /// <summary>
    /// Manually set rate limit retry time (from API response headers)
    /// </summary>
    public void SetRateLimitRetryAfter(int seconds)
    {
        _rateLimitRetryAfterSeconds = seconds;
        Logger.LogInformation(
            "[LLM Handler] Rate limit retry-after set to {Seconds}s",
            seconds);
    }
}
