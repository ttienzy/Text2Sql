namespace TextToSqlAgent.Core.Models;

public class NormalizedPrompt
{
    public string OriginalPrompt { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}