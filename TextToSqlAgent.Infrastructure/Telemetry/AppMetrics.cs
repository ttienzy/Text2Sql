using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TextToSqlAgent.Infrastructure.Telemetry;

/// <summary>
/// IMP-4: Custom OpenTelemetry metrics for the TextToSqlAgent pipeline.
/// Provides counters, histograms, and gauges for monitoring agent performance.
/// 
/// Usage: Inject AppMetrics and call recording methods at each pipeline stage.
/// These metrics are automatically exported via the OpenTelemetry SDK when configured.
/// </summary>
public class AppMetrics
{
    public static readonly string ServiceName = "TextToSqlAgent";
    public static readonly string ServiceVersion = "1.0.0";

    private readonly Meter _meter;

    // -- Counters --
    private readonly Counter<long> _queryTotal;
    private readonly Counter<long> _llmCallsTotal;
    private readonly Counter<long> _sqlCorrectionTotal;
    private readonly Counter<long> _cacheHitTotal;
    private readonly Counter<long> _cacheMissTotal;
    private readonly Counter<long> _errorTotal;

    // -- Histograms --
    private readonly Histogram<double> _queryDuration;
    private readonly Histogram<double> _llmCallDuration;
    private readonly Histogram<double> _schemaRetrievalDuration;
    private readonly Histogram<double> _sqlExecutionDuration;

    // -- Gauges (via UpDownCounter) --
    private readonly UpDownCounter<long> _activeQueries;

    public AppMetrics()
    {
        _meter = new Meter(ServiceName, ServiceVersion);

        // Counters
        _queryTotal = _meter.CreateCounter<long>(
            "agent.query.total",
            description: "Total number of queries processed");

        _llmCallsTotal = _meter.CreateCounter<long>(
            "agent.llm.calls.total",
            description: "Total LLM API calls made");

        _sqlCorrectionTotal = _meter.CreateCounter<long>(
            "agent.sql.correction.total",
            description: "Total SQL correction attempts");

        _cacheHitTotal = _meter.CreateCounter<long>(
            "agent.cache.hit.total",
            description: "Total cache hits");

        _cacheMissTotal = _meter.CreateCounter<long>(
            "agent.cache.miss.total",
            description: "Total cache misses");

        _errorTotal = _meter.CreateCounter<long>(
            "agent.error.total",
            description: "Total errors encountered");

        // Histograms
        _queryDuration = _meter.CreateHistogram<double>(
            "agent.query.duration",
            unit: "ms",
            description: "End-to-end query processing duration");

        _llmCallDuration = _meter.CreateHistogram<double>(
            "agent.llm.call.duration",
            unit: "ms",
            description: "Individual LLM call duration");

        _schemaRetrievalDuration = _meter.CreateHistogram<double>(
            "agent.schema.retrieval.duration",
            unit: "ms",
            description: "Schema retrieval duration from Qdrant");

        _sqlExecutionDuration = _meter.CreateHistogram<double>(
            "agent.sql.execution.duration",
            unit: "ms",
            description: "SQL query execution duration against target DB");

        // Gauges
        _activeQueries = _meter.CreateUpDownCounter<long>(
            "agent.query.active",
            description: "Currently active queries being processed");
    }

    // -- Recording methods --

    public void RecordQueryStart(string pipelineType = "query")
    {
        _queryTotal.Add(1, new KeyValuePair<string, object?>("pipeline", pipelineType));
        _activeQueries.Add(1);
    }

    public void RecordQueryEnd(double durationMs, string pipelineType = "query", bool success = true)
    {
        _queryDuration.Record(durationMs, new KeyValuePair<string, object?>("pipeline", pipelineType));
        _activeQueries.Add(-1);
        if (!success) _errorTotal.Add(1, new KeyValuePair<string, object?>("pipeline", pipelineType));
    }

    public void RecordLlmCall(double durationMs, string provider = "openai", string model = "gpt-4o-mini")
    {
        _llmCallsTotal.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model));
        _llmCallDuration.Record(durationMs,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("model", model));
    }

    public void RecordSchemaRetrieval(double durationMs)
        => _schemaRetrievalDuration.Record(durationMs);

    public void RecordSqlExecution(double durationMs, bool success = true)
    {
        _sqlExecutionDuration.Record(durationMs);
        if (!success) _errorTotal.Add(1, new KeyValuePair<string, object?>("type", "sql_execution"));
    }

    public void RecordSqlCorrection(int attempt)
        => _sqlCorrectionTotal.Add(1, new KeyValuePair<string, object?>("attempt", attempt));

    public void RecordCacheHit(string cacheType = "query_plan")
        => _cacheHitTotal.Add(1, new KeyValuePair<string, object?>("type", cacheType));

    public void RecordCacheMiss(string cacheType = "query_plan")
        => _cacheMissTotal.Add(1, new KeyValuePair<string, object?>("type", cacheType));

    public void RecordError(string errorType)
        => _errorTotal.Add(1, new KeyValuePair<string, object?>("type", errorType));
}
