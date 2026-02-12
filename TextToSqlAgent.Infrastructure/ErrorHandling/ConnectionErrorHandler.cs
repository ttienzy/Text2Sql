using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Exceptions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.ErrorHandling;

/// <summary>
/// Handles database connection errors with intelligent retry
/// </summary>
public class ConnectionErrorHandler : BaseErrorHandler
{
    private readonly SqlErrorAnalyzer _errorAnalyzer;
    private int _consecutiveFailures = 0;
    private DateTime? _circuitOpenedAt;
    private const int CircuitBreakerThreshold = 5;
    private const int CircuitBreakerResetSeconds = 60;

    public ConnectionErrorHandler(
        ILogger<ConnectionErrorHandler> logger,
        SqlErrorAnalyzer errorAnalyzer) 
        : base(logger)
    {
        _errorAnalyzer = errorAnalyzer;
    }

    /// <summary>
    /// Handle connection errors with circuit breaker pattern
    /// </summary>
    public async Task<T> HandleConnectionErrorAsync<T>(
        Func<Task<T>> operation,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        // Check circuit breaker state
        if (IsCircuitOpen())
        {
            Logger.LogError(
                "[Connection Handler] Circuit breaker is OPEN. Failing fast. Opened at: {OpenedAt}",
                _circuitOpenedAt);
            
            throw new DatabaseConnectionException(
                "Circuit breaker is open due to consecutive connection failures. " +
                "Please check database connectivity.",
                exception);
        }

        // Analyze the error
        var sqlError = _errorAnalyzer.AnalyzeError(exception.Message, string.Empty);

        try
        {
            var result = await HandleAsync(operation, sqlError, cancellationToken);
            
            // Success - reset circuit breaker
            ResetCircuitBreaker();
            
            return result;
        }
        catch (Exception ex)
        {
            // Failure - increment counter
            IncrementFailureCount();
            
            Logger.LogError(
                ex,
                "[Connection Handler] Connection failed. Consecutive failures: {Count}",
                _consecutiveFailures);

            throw;
        }
    }

    protected override async Task<T> RetryWithCircuitBreakerAsync<T>(
        Func<Task<T>> operation,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            // Check if circuit should be opened
            if (_consecutiveFailures >= CircuitBreakerThreshold)
            {
                OpenCircuit();
                throw new DatabaseConnectionException(
                    $"Circuit breaker opened after {CircuitBreakerThreshold} consecutive failures",
                    lastException);
            }

            try
            {
                attempt++;
                var delaySeconds = Math.Pow(2, attempt);
                
                Logger.LogDebug(
                    "[{Handler}] Circuit breaker retry {Attempt}/{Max} after {Delay}s",
                    "Connection Handler",
                    attempt,
                    maxRetries,
                    delaySeconds);

                if (attempt > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }

                return await operation();
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logger.LogWarning(
                    ex,
                    "[Connection Handler] Retry {Attempt} failed",
                    attempt);

                if (attempt >= maxRetries)
                    break;
            }
        }

        throw lastException!;
    }

    private bool IsCircuitOpen()
    {
        if (_circuitOpenedAt == null)
            return false;

        var elapsed = DateTime.UtcNow - _circuitOpenedAt.Value;
        
        if (elapsed.TotalSeconds >= CircuitBreakerResetSeconds)
        {
            Logger.LogDebug(
                "[Connection Handler] Circuit breaker reset after {Seconds}s",
                CircuitBreakerResetSeconds);
            
            ResetCircuitBreaker();
            return false;
        }

        return true;
    }

    private void OpenCircuit()
    {
        _circuitOpenedAt = DateTime.UtcNow;
        Logger.LogError(
            "[Connection Handler] Circuit breaker OPENED at {Time}",
            _circuitOpenedAt);
    }

    private void ResetCircuitBreaker()
    {
        if (_consecutiveFailures > 0 || _circuitOpenedAt != null)
        {
            Logger.LogDebug("[Connection Handler] Circuit breaker RESET");
        }
        
        _consecutiveFailures = 0;
        _circuitOpenedAt = null;
    }

    private void IncrementFailureCount()
    {
        _consecutiveFailures++;
    }

    protected override Exception CreateException(SqlError error)
    {
        return error.Type switch
        {
            SqlErrorType.ConnectionTimeout => new DatabaseTimeoutException(error.ErrorMessage),
            SqlErrorType.ConnectionFailed => new DatabaseConnectionException(error.ErrorMessage),
            SqlErrorType.ConnectionRefused => new DatabaseConnectionException(error.ErrorMessage),
            SqlErrorType.NetworkError => new DatabaseConnectionException(error.ErrorMessage),
            SqlErrorType.DatabaseAccessDenied => new DatabasePermissionException(error.ErrorMessage),
            _ => base.CreateException(error)
        };
    }

    /// <summary>
    /// Get current circuit breaker status
    /// </summary>
    public string GetCircuitBreakerStatus()
    {
        if (IsCircuitOpen())
        {
            var elapsed = DateTime.UtcNow - _circuitOpenedAt!.Value;
            var remaining = CircuitBreakerResetSeconds - elapsed.TotalSeconds;
            return $"OPEN (resets in {remaining:F0}s)";
        }

        return $"CLOSED (failures: {_consecutiveFailures}/{CircuitBreakerThreshold})";
    }
}
