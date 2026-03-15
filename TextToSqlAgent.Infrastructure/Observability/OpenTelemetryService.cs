using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TextToSqlAgent.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry service with custom spans for ReAct agent steps
/// Provides distributed tracing for:
/// - tool_selection
/// - tool_execution
/// - sql_generation
/// - sql_execution
/// - reflection
/// </summary>
public class OpenTelemetryService : IDisposable
{
    private readonly ILogger<OpenTelemetryService> _logger;
    private readonly TracerProvider? _tracerProvider;
    private readonly OpenTelemetryOptions _options;
    private ActivitySource? _activitySource;
    private bool _disposed;

    public OpenTelemetryService(
        ILogger<OpenTelemetryService> logger,
        OpenTelemetryOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new OpenTelemetryOptions();

        // Only create tracer provider if enabled
        if (_options.EnableTracing)
        {
            _tracerProvider = CreateTracerProvider();
            _activitySource = new ActivitySource(_options.ServiceName);
            _logger.LogInformation(
                "OpenTelemetry initialized with exporter: {Exporter}",
                _options.TracingExporter);
        }
        else
        {
            _logger.LogInformation("OpenTelemetry tracing is disabled");
        }
    }

    private TracerProvider CreateTracerProvider()
    {
        var builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(_options.ServiceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["service.version"] = _options.ServiceVersion,
                    ["deployment.environment"] = _options.Environment
                }));

        // Add console exporter for development
        if (_options.TracingExporter.ToLower() == "console")
        {
            builder.AddConsoleExporter();
        }

        return builder.Build();
    }

    #region ReAct Step Spans

    /// <summary>
    /// Track tool selection step
    /// </summary>
    public IDisposable? TrackToolSelection(string toolName, string reasoning)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("tool_selection", ActivityKind.Client);
        if (activity == null) return null;

        activity.SetTag("tool_selection.tool_name", toolName);
        activity.SetTag("tool_selection.reasoning", reasoning);
        activity.SetTag("reagent.step_type", "selection");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track tool execution step with circuit breaker context
    /// </summary>
    public IDisposable? TrackToolExecution(
        string toolName,
        Dictionary<string, object>? parameters = null,
        bool circuitBreakerOpen = false)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("tool_execution", ActivityKind.Client);
        if (activity == null) return null;

        activity.SetTag("tool_execution.tool_name", toolName);
        activity.SetTag("tool_execution.circuit_breaker_open", circuitBreakerOpen);

        if (parameters != null)
        {
            foreach (var param in parameters.Take(10))
            {
                activity.SetTag($"tool_execution.param.{param.Key}",
                    param.Value?.ToString() ?? "null");
            }
        }

        activity.SetTag("reagent.step_type", "execution");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track SQL generation step
    /// </summary>
    public IDisposable? TrackSqlGeneration(
        string question,
        string? schemaContext = null,
        int estimatedTokens = 0)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("sql_generation", ActivityKind.Client);
        if (activity == null) return null;

        activity.SetTag("sql_generation.question", question);
        activity.SetTag("sql_generation.schema_context_available", !string.IsNullOrEmpty(schemaContext));
        activity.SetTag("sql_generation.estimated_tokens", estimatedTokens);
        activity.SetTag("reagent.step_type", "generation");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track SQL execution step
    /// </summary>
    public IDisposable? TrackSqlExecution(
        string sql,
        string database,
        int? rowCount = null,
        long? executionTimeMs = null)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("sql_execution", ActivityKind.Client);
        if (activity == null) return null;

        activity.SetTag("sql_execution.sql", sql.Length > 500 ? sql[..500] + "..." : sql);
        activity.SetTag("sql_execution.database", database);

        if (rowCount.HasValue)
            activity.SetTag("sql_execution.row_count", rowCount.Value);

        if (executionTimeMs.HasValue)
            activity.SetTag("sql_execution.execution_time_ms", executionTimeMs.Value);

        activity.SetTag("reagent.step_type", "execution");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track reflection/reasoning step
    /// </summary>
    public IDisposable? TrackReflection(
        int stepNumber,
        string assessment,
        bool shouldTerminate,
        string? terminationReason = null)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("reflection", ActivityKind.Internal);
        if (activity == null) return null;

        activity.SetTag("reflection.step_number", stepNumber);
        activity.SetTag("reflection.assessment", assessment);
        activity.SetTag("reflection.should_terminate", shouldTerminate);

        if (!string.IsNullOrEmpty(terminationReason))
            activity.SetTag("reflection.termination_reason", terminationReason);

        activity.SetTag("reagent.step_type", "reflection");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track reasoning/thinking step
    /// </summary>
    public IDisposable? TrackReasoning(string thought, string? plan = null)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("reasoning", ActivityKind.Internal);
        if (activity == null) return null;

        activity.SetTag("reasoning.thought", thought.Length > 1000 ? thought[..1000] + "..." : thought);

        if (!string.IsNullOrEmpty(plan))
            activity.SetTag("reasoning.plan", plan.Length > 500 ? plan[..500] + "..." : plan);

        activity.SetTag("reagent.step_type", "reasoning");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track LLM call with detailed attributes
    /// </summary>
    public IDisposable? TrackLlmCall(
        string model,
        string promptType,
        int? promptTokens = null,
        int? completionTokens = null)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("llm.call", ActivityKind.Client);
        if (activity == null) return null;

        activity.SetTag("llm.model", model);
        activity.SetTag("llm.prompt_type", promptType);

        if (promptTokens.HasValue)
            activity.SetTag("llm.prompt_tokens", promptTokens.Value);

        if (completionTokens.HasValue)
            activity.SetTag("llm.completion_tokens", completionTokens.Value);

        activity.SetTag("reagent.component", "llm");

        return new SpanDisposer(activity);
    }

    /// <summary>
    /// Track overall agent execution
    /// </summary>
    public IDisposable? TrackAgentExecution(string question, string? databaseId = null)
    {
        if (_activitySource == null) return null;

        var activity = _activitySource.StartActivity("agent.execution", ActivityKind.Server);
        if (activity == null) return null;

        activity.SetTag("agent.question", question.Length > 500 ? question[..500] + "..." : question);

        if (!string.IsNullOrEmpty(databaseId))
            activity.SetTag("agent.database_id", databaseId);

        activity.SetTag("reagent.component", "agent");

        return new SpanDisposer(activity);
    }

    #endregion

    #region Metrics

    /// <summary>
    /// Record a custom metric (gauge)
    /// </summary>
    public void RecordMetric(string name, double value, Dictionary<string, string>? tags = null)
    {
        _logger.LogDebug("Metric {Name}: {Value}", name, value);
    }

    /// <summary>
    /// Add event to current span
    /// </summary>
    public void AddSpanEvent(string name, Dictionary<string, object>? attributes = null)
    {
        _logger.LogDebug("Span event: {Name}", name);
    }

    /// <summary>
    /// Set span status (error)
    /// </summary>
    public void SetSpanError(string message, Exception? exception = null)
    {
        _logger.LogWarning("Span error: {Message}", message);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _tracerProvider?.Dispose();
        _activitySource?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Helper class to dispose activity
/// </summary>
internal class SpanDisposer : IDisposable
{
    private readonly Activity? _activity;

    public SpanDisposer(Activity? activity)
    {
        _activity = activity;
    }

    public void Dispose()
    {
        _activity?.Stop();
    }
}

/// <summary>
/// OpenTelemetry configuration options
/// </summary>
public class OpenTelemetryOptions
{
    public bool EnableTracing { get; set; } = true;
    public string ServiceName { get; set; } = "TextToSqlAgent";
    public string ServiceVersion { get; set; } = "1.0.0";
    public string Environment { get; set; } = "development";
    public string TracingExporter { get; set; } = "console"; // console, jaeger, otlp
    public string JaegerEndpoint { get; set; } = "http://localhost:4317";
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public double SampleRate { get; set; } = 1.0;
}
