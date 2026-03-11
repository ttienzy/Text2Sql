using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace TextToSqlAgent.Infrastructure.Observability;

/// <summary>
/// Centralized telemetry service for tracking metrics and events
/// </summary>
public class TelemetryService
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly MetricsCollector _metricsCollector;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        MetricsCollector metricsCollector)
    {
        _logger = logger;
        _metricsCollector = metricsCollector;
    }

    /// <summary>
    /// Track agent execution
    /// </summary>
    public IDisposable TrackAgentExecution(string question, string? databaseId = null)
    {
        var activity = new TelemetryActivity("AgentExecution", _logger, _metricsCollector);
        activity.AddProperty("question", question);
        activity.AddProperty("database", databaseId ?? "unknown");
        return activity;
    }

    /// <summary>
    /// Track tool execution
    /// </summary>
    public IDisposable TrackToolExecution(string toolName, Dictionary<string, object>? parameters = null)
    {
        var activity = new TelemetryActivity($"Tool.{toolName}", _logger, _metricsCollector);
        activity.AddProperty("tool_name", toolName);

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                activity.AddProperty($"param_{param.Key}", param.Value?.ToString() ?? "null");
            }
        }

        return activity;
    }

    /// <summary>
    /// Track LLM call
    /// </summary>
    public IDisposable TrackLLMCall(string model, string promptType, int estimatedTokens = 0)
    {
        var activity = new TelemetryActivity("LLM.Call", _logger, _metricsCollector);
        activity.AddProperty("model", model);
        activity.AddProperty("prompt_type", promptType);
        activity.AddProperty("estimated_tokens", estimatedTokens);
        return activity;
    }

    /// <summary>
    /// Track database query
    /// </summary>
    public IDisposable TrackDatabaseQuery(string database, string queryType = "SELECT")
    {
        var activity = new TelemetryActivity("Database.Query", _logger, _metricsCollector);
        activity.AddProperty("database", database);
        activity.AddProperty("query_type", queryType);
        return activity;
    }

    /// <summary>
    /// Track schema linking
    /// </summary>
    public IDisposable TrackSchemaLinking(string question, int schemaSize)
    {
        var activity = new TelemetryActivity("SchemaLinking", _logger, _metricsCollector);
        activity.AddProperty("question", question);
        activity.AddProperty("schema_size", schemaSize);
        return activity;
    }

    /// <summary>
    /// Log error with context
    /// </summary>
    public void LogError(Exception ex, string operation, Dictionary<string, object>? context = null)
    {
        var properties = new Dictionary<string, object>
        {
            ["operation"] = operation,
            ["error_type"] = ex.GetType().Name,
            ["error_message"] = ex.Message,
            ["stack_trace"] = ex.StackTrace ?? ""
        };

        if (context != null)
        {
            foreach (var kvp in context)
            {
                properties[$"context_{kvp.Key}"] = kvp.Value;
            }
        }

        _logger.LogError(ex, "Operation {Operation} failed: {ErrorMessage}", operation, ex.Message);
        _metricsCollector.RecordError(operation, ex.GetType().Name);
    }

    /// <summary>
    /// Log warning with context
    /// </summary>
    public void LogWarning(string message, string operation, Dictionary<string, object>? context = null)
    {
        _logger.LogWarning("Operation {Operation}: {Message}", operation, message);

        if (context != null)
        {
            foreach (var kvp in context)
            {
                _logger.LogDebug("  {Key}: {Value}", kvp.Key, kvp.Value);
            }
        }
    }

    /// <summary>
    /// Record custom metric
    /// </summary>
    public void RecordMetric(string metricName, double value, Dictionary<string, string>? tags = null)
    {
        _metricsCollector.RecordMetric(metricName, value, tags);
        _logger.LogDebug("Metric {MetricName}: {Value}", metricName, value);
    }
}

/// <summary>
/// Disposable activity for tracking operation duration and outcome
/// </summary>
internal class TelemetryActivity : IDisposable
{
    private readonly string _operationName;
    private readonly ILogger _logger;
    private readonly MetricsCollector _metricsCollector;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, object> _properties;
    private bool _success = true;
    private string? _errorMessage;

    public TelemetryActivity(string operationName, ILogger logger, MetricsCollector metricsCollector)
    {
        _operationName = operationName;
        _logger = logger;
        _metricsCollector = metricsCollector;
        _stopwatch = Stopwatch.StartNew();
        _properties = new Dictionary<string, object>();

        _logger.LogDebug("[{Operation}] Started", _operationName);
    }

    public void AddProperty(string key, object value)
    {
        _properties[key] = value;
    }

    public void SetSuccess(bool success, string? errorMessage = null)
    {
        _success = success;
        _errorMessage = errorMessage;
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        var duration = _stopwatch.ElapsedMilliseconds;

        if (_success)
        {
            _logger.LogInformation(
                "[{Operation}] Completed in {Duration}ms | {Properties}",
                _operationName,
                duration,
                FormatProperties());
        }
        else
        {
            _logger.LogError(
                "[{Operation}] Failed in {Duration}ms | Error: {Error} | {Properties}",
                _operationName,
                duration,
                _errorMessage ?? "Unknown error",
                FormatProperties());
        }

        // Record metrics
        _metricsCollector.RecordDuration(_operationName, duration);

        if (!_success)
        {
            _metricsCollector.RecordError(_operationName, _errorMessage ?? "Unknown");
        }
    }

    private string FormatProperties()
    {
        if (_properties.Count == 0)
            return "{}";

        var props = string.Join(", ", _properties.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"{{{props}}}";
    }
}
