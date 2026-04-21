using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services.Visualization;

public interface IPythonVisualizer
{
    Task<VisualizationResult> GenerateChartAsync(string question, List<Dictionary<string, object?>> data, CancellationToken ct = default);
}

public class VisualizationResult
{
    public string? ImageBase64 { get; set; }
    public string ChartType { get; set; } = "none";
    public bool ShouldDisplay { get; set; }
    public string? Reason { get; set; }
}
