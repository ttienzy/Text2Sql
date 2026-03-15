using Polly;
using Polly.CircuitBreaker;
using Microsoft.Extensions.Logging;

namespace TextToSqlAgent.Infrastructure.Resilience;

/// <summary>
/// Circuit Breaker wrapper for tool execution using Polly v8
/// Prevents cascading failures when a tool is failing repeatedly
/// </summary>
public class ToolCircuitBreaker
{
    private readonly Dictionary<string, CircuitBreakerPipeline> _pipelines = new();
    private readonly ILogger<ToolCircuitBreaker> _logger;
    private readonly CircuitBreakerOptions _options;

    public ToolCircuitBreaker(ILogger<ToolCircuitBreaker> logger, CircuitBreakerOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new CircuitBreakerOptions();
    }

    /// <summary>
    /// Execute an action with circuit breaker protection
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        string toolName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        var pipeline = GetOrCreatePipeline(toolName);

        try
        {
            return await pipeline.Pipeline.ExecuteAsync(async ct2 => await action(ct2), ct);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(
                "Circuit breaker is OPEN for tool {ToolName}. Last exception: {Message}",
                toolName, ex.Message);
            throw new CircuitBreakerOpenException(
                $"Circuit breaker is OPEN for tool '{toolName}'. The tool is currently unavailable.",
                ex);
        }
    }

    /// <summary>
    /// Execute an action with circuit breaker protection (non-generic version)
    /// </summary>
    public async Task ExecuteAsync(
        string toolName,
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        var pipeline = GetOrCreatePipeline(toolName);

        try
        {
            await pipeline.Pipeline.ExecuteAsync(async ct2 => await action(ct2), ct);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(
                "Circuit breaker is OPEN for tool {ToolName}. Last exception: {Message}",
                toolName, ex.Message);
            throw new CircuitBreakerOpenException(
                $"Circuit breaker is OPEN for tool '{toolName}'. The tool is currently unavailable.",
                ex);
        }
    }

    /// <summary>
    /// Get circuit breaker state for a specific tool
    /// </summary>
    public CircuitBreakerState GetState(string toolName)
    {
        if (_pipelines.TryGetValue(toolName, out var pipeline))
        {
            return new CircuitBreakerState
            {
                ToolName = toolName,
                IsOpen = pipeline.BreakerController.IsOpened,
                LastFailure = pipeline.LastFailure,
                FailureCount = pipeline.FailureCount
            };
        }

        return new CircuitBreakerState { ToolName = toolName, IsOpen = false };
    }

    /// <summary>
    /// Get all circuit breaker states
    /// </summary>
    public IReadOnlyDictionary<string, CircuitBreakerState> GetAllStates()
    {
        return _pipelines.ToDictionary(
            kvp => kvp.Key,
            kvp => GetState(kvp.Key));
    }

    /// <summary>
    /// Reset all circuit breakers
    /// </summary>
    public void ResetAll()
    {
        _pipelines.Clear();
        _logger.LogInformation("All circuit breakers have been reset");
    }

    private CircuitBreakerPipeline GetOrCreatePipeline(string toolName)
    {
        if (_pipelines.TryGetValue(toolName, out var existing))
        {
            return existing;
        }

        // Create new pipeline with Polly v8 syntax
        var pipeline = new CircuitBreakerPipeline
        {
            Pipeline = new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = _options.FailureRatio,
                    MinimumThroughput = _options.MinimumThroughput,
                    SamplingDuration = _options.SamplingDuration,
                    BreakDuration = _options.BreakDuration,
                    OnOpened = args =>
                    {
                        _logger.LogWarning(
                            "Circuit breaker OPENED for tool {ToolName}. Will retry after {Duration}",
                            toolName, _options.BreakDuration);
                        return default;
                    },
                    OnClosed = args =>
                    {
                        _logger.LogInformation(
                            "Circuit breaker CLOSED for tool {ToolName}. Normal operation resumed.",
                            toolName);
                        return default;
                    },
                    OnHalfOpened = args =>
                    {
                        _logger.LogInformation(
                            "Circuit breaker HALF-OPEN for tool {ToolName}. Testing if recovery is possible.",
                            toolName);
                        return default;
                    }
                })
                .Build(),
            BreakerController = new SimpleBreakerController()
        };

        _pipelines[toolName] = pipeline;
        _logger.LogDebug("Created circuit breaker pipeline for tool: {ToolName}", toolName);

        return pipeline;
    }
}

/// <summary>
/// Options for circuit breaker configuration
/// </summary>
public class CircuitBreakerOptions
{
    public double FailureRatio { get; set; } = 0.5; // 50% failure ratio triggers open
    public int MinimumThroughput { get; set; } = 5; // Minimum calls before evaluating
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Internal pipeline wrapper
/// </summary>
internal class CircuitBreakerPipeline
{
    public ResiliencePipeline Pipeline { get; set; } = null!;
    public SimpleBreakerController BreakerController { get; set; } = new();
    public Exception? LastFailure { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Simple breaker state tracker
/// </summary>
internal class SimpleBreakerController
{
    private bool _isOpened;
    private DateTime? _openedAt;

    public bool IsOpened => _isOpened && (_openedAt == null || DateTime.UtcNow - _openedAt.Value > TimeSpan.FromSeconds(30));

    public void MarkOpened()
    {
        _isOpened = true;
        _openedAt = DateTime.UtcNow;
    }

    public void MarkClosed()
    {
        _isOpened = false;
        _openedAt = null;
    }
}

/// <summary>
/// Circuit breaker state information
/// </summary>
public class CircuitBreakerState
{
    public string ToolName { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public Exception? LastFailure { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Exception thrown when circuit breaker is open
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception inner) : base(message, inner) { }
}
