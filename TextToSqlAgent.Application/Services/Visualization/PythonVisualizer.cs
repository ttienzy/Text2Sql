using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TextToSqlAgent.Application.Services.Visualization;

public class PythonVisualizer : IPythonVisualizer
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonVisualizer> _logger;
    private readonly string _sidecarUrl;
    private readonly int _timeoutMs;

    public PythonVisualizer(IConfiguration configuration, ILogger<PythonVisualizer> logger)
    {
        _logger = logger;
        
        _sidecarUrl = configuration["PYTHON_SIDECAR_URL"]
            ?? Environment.GetEnvironmentVariable("PYTHON_SIDECAR_URL")
            ?? "http://localhost:8100";

        // Higher timeout for visualization since Matplotlib rendering can take 1-2s
        _timeoutMs = int.TryParse(
            configuration["PYTHON_VISUALIZER_TIMEOUT_MS"]
            ?? Environment.GetEnvironmentVariable("PYTHON_VISUALIZER_TIMEOUT_MS"),
            out var ms) ? ms : 3000;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_sidecarUrl),
            Timeout = TimeSpan.FromMilliseconds(_timeoutMs)
        };
    }

    public async Task<VisualizationResult> GenerateChartAsync(
        string question, 
        List<Dictionary<string, object?>> data, 
        CancellationToken ct = default)
    {
        try
        {
            var request = new PythonVisualizeRequest
            {
                Question = question,
                Data = data
            };

            var response = await _httpClient.PostAsJsonAsync("/api/visualize", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PythonVisualizeResponse>(cancellationToken: ct);
            if (result == null)
            {
                return new VisualizationResult { ShouldDisplay = false, Reason = "Empty response from sidecar" };
            }

            return new VisualizationResult
            {
                ImageBase64 = result.ImageBase64,
                ChartType = result.ChartType ?? "none",
                ShouldDisplay = result.ShouldDisplay,
                Reason = result.Reason
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PythonVisualizer] Failed to generate chart: {Message}", ex.Message);
            return new VisualizationResult { ShouldDisplay = false, Reason = "Sidecar error or timeout" };
        }
    }

    private class PythonVisualizeRequest
    {
        [JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public List<Dictionary<string, object?>> Data { get; set; } = new();

        [JsonPropertyName("chart_type")]
        public string ChartType { get; set; } = "auto";
    }

    private class PythonVisualizeResponse
    {
        [JsonPropertyName("image_base64")]
        public string? ImageBase64 { get; set; }

        [JsonPropertyName("chart_type")]
        public string? ChartType { get; set; }

        [JsonPropertyName("should_display")]
        public bool ShouldDisplay { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
