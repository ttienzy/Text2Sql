using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Python sidecar-based intent classifier.
/// Delegates classification to the Python sidecar when it is enterprise-ready,
/// and falls back to the native C# classifier when the sidecar is degraded or unavailable.
/// </summary>
public class PythonIntentClassifier : IIntentClassifier
{
    private readonly HttpClient _httpClient;
    private readonly IntentClassifier _fallbackClassifier;
    private readonly ILogger<PythonIntentClassifier> _logger;
    private readonly string _sidecarUrl;
    private readonly int _timeoutMs;

    public PythonIntentClassifier(
        IntentClassifier fallbackClassifier,
        IConfiguration configuration,
        ILogger<PythonIntentClassifier> logger)
    {
        _fallbackClassifier = fallbackClassifier;
        _logger = logger;

        _sidecarUrl = configuration["PYTHON_SIDECAR_URL"]
            ?? Environment.GetEnvironmentVariable("PYTHON_SIDECAR_URL")
            ?? "http://localhost:8100";

        _timeoutMs = int.TryParse(
            configuration["PYTHON_SIDECAR_TIMEOUT_MS"]
            ?? Environment.GetEnvironmentVariable("PYTHON_SIDECAR_TIMEOUT_MS"),
            out var ms) ? ms : 500;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_sidecarUrl),
            Timeout = TimeSpan.FromMilliseconds(_timeoutMs + 200)
        };

        _logger.LogInformation(
            "[PythonIntentClassifier] Initialized with sidecar={Url}, timeout={Timeout}ms",
            _sidecarUrl, _timeoutMs);
    }

    public async Task<IntentClassificationResult> ClassifyAsync(
        string question,
        string? conversationHistory = null,
        string? databaseContext = null,
        CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(_timeoutMs));

            var request = new PythonClassifyRequest
            {
                Question = question,
                Language = "auto"
            };

            var response = await _httpClient.PostAsJsonAsync("/api/classify", request, cts.Token);
            response.EnsureSuccessStatusCode();

            var pythonResult = await response.Content
                .ReadFromJsonAsync<PythonClassifyResponse>(cancellationToken: cts.Token);

            if (pythonResult == null)
            {
                _logger.LogWarning("[PythonIntentClassifier] Null response from sidecar, falling back to C#");
                return await _fallbackClassifier.ClassifyAsync(question, conversationHistory, databaseContext, ct);
            }

            if (ShouldUseDotNetFallback(pythonResult))
            {
                _logger.LogWarning(
                    "[PythonIntentClassifier] Sidecar returned state={State}, mode={Mode}, advisory={Advisory}, fallbackReason={Reason}. Falling back to C# classifier.",
                    pythonResult.ServiceState,
                    pythonResult.ClassifierMode,
                    pythonResult.AdvisoryOnly,
                    pythonResult.FallbackReason ?? "n/a");
                return await _fallbackClassifier.ClassifyAsync(question, conversationHistory, databaseContext, ct);
            }

            var result = MapToIntentClassificationResult(pythonResult, question);

            _logger.LogInformation(
                "[PythonIntentClassifier] Classified '{Question}' -> {Intent} (conf={Confidence:F2}, method=Python-{Mode}, state={State}, advisory={Advisory})",
                question.Length > 40 ? question[..40] + "..." : question,
                result.Intent,
                result.Confidence,
                pythonResult.ClassifierMode,
                pythonResult.ServiceState,
                pythonResult.AdvisoryOnly);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "[PythonIntentClassifier] Sidecar timeout ({Timeout}ms), falling back to C# classifier",
                _timeoutMs);
            return await _fallbackClassifier.ClassifyAsync(question, conversationHistory, databaseContext, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "[PythonIntentClassifier] Sidecar unavailable ({Error}), falling back to C# classifier",
                ex.Message);
            return await _fallbackClassifier.ClassifyAsync(question, conversationHistory, databaseContext, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PythonIntentClassifier] Unexpected error, falling back to C# classifier");
            return await _fallbackClassifier.ClassifyAsync(question, conversationHistory, databaseContext, ct);
        }
    }

    private IntentClassificationResult MapToIntentClassificationResult(
        PythonClassifyResponse pythonResult,
        string originalQuestion)
    {
        var (intent, route, risk) = MapIntentString(pythonResult.Intent);
        var matchedKeywords = new List<string> { $"python-sidecar:{pythonResult.ClassifierMode}:{pythonResult.Intent.ToLowerInvariant()}" };
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(pythonResult.FallbackReason))
        {
            warnings.Add($"python-sidecar:{pythonResult.FallbackReason}");
        }

        if (pythonResult.AdvisoryOnly)
        {
            warnings.Add("python-sidecar:advisory-only");
        }

        return new IntentClassificationResult
        {
            Intent = intent,
            Route = route,
            RiskLevel = risk,
            Confidence = pythonResult.Confidence,
            NormalizedQuery = originalQuestion,
            Reasoning = $"Python sidecar: intent={pythonResult.Intent}, lang={pythonResult.DetectedLanguage}, mode={pythonResult.ClassifierMode}, state={pythonResult.ServiceState}, model={pythonResult.ModelVersion}",
            Method = pythonResult.ClassifierMode == "ml" ? ClassificationMethod.Hybrid : ClassificationMethod.RuleBased,
            SubIntent = pythonResult.SubIntent,
            MatchedKeywords = matchedKeywords,
            DetectedEntities = new List<string>(),
            Warnings = warnings,
        };
    }

    internal static bool ShouldUseDotNetFallback(
        string classifierMode,
        string serviceState,
        bool advisoryOnly)
    {
        if (string.Equals(classifierMode, "safety_override", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (advisoryOnly)
        {
            return true;
        }

        if (!string.Equals(serviceState, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(classifierMode, "rule_fallback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldUseDotNetFallback(PythonClassifyResponse pythonResult) =>
        ShouldUseDotNetFallback(
            pythonResult.ClassifierMode,
            pythonResult.ServiceState,
            pythonResult.AdvisoryOnly);

    private static (IntentCategory Intent, PipelineRoute Route, RiskLevel Risk) MapIntentString(string pythonIntent)
    {
        return pythonIntent.ToUpperInvariant() switch
        {
            "SELECT" => (IntentCategory.Query, PipelineRoute.Query, RiskLevel.Low),
            "AGGREGATE" => (IntentCategory.Query, PipelineRoute.Query, RiskLevel.Low),
            "SCHEMA_QUERY" => (IntentCategory.Query, PipelineRoute.Query, RiskLevel.Low),
            "WRITE_INSERT" => (IntentCategory.Insert, PipelineRoute.Dml, RiskLevel.Medium),
            "WRITE_UPDATE" => (IntentCategory.Update, PipelineRoute.Dml, RiskLevel.High),
            "WRITE_DELETE" => (IntentCategory.Delete, PipelineRoute.Dml, RiskLevel.Critical),
            "DDL" => (IntentCategory.Forbidden, PipelineRoute.Forbidden, RiskLevel.Critical),
            "AMBIGUOUS" => (IntentCategory.Unknown, PipelineRoute.Query, RiskLevel.Low),
            _ => (IntentCategory.Query, PipelineRoute.Query, RiskLevel.Low),
        };
    }

    private class PythonClassifyRequest
    {
        [JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "auto";
    }

    private class PythonClassifyResponse
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("is_write_operation")]
        public bool IsWriteOperation { get; set; }

        [JsonPropertyName("requires_confirmation")]
        public bool RequiresConfirmation { get; set; }

        [JsonPropertyName("sub_intent")]
        public string? SubIntent { get; set; }

        [JsonPropertyName("detected_language")]
        public string DetectedLanguage { get; set; } = "en";

        [JsonPropertyName("classifier_mode")]
        public string ClassifierMode { get; set; } = "rule_fallback";

        [JsonPropertyName("service_state")]
        public string ServiceState { get; set; } = "not_ready";

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; set; } = "not_loaded";

        [JsonPropertyName("advisory_only")]
        public bool AdvisoryOnly { get; set; } = true;

        [JsonPropertyName("fallback_reason")]
        public string? FallbackReason { get; set; }
    }
}
