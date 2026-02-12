namespace TextToSqlAgent.Core.Models;

public class CorrectionAttempt
{
    public int AttemptNumber { get; set; }
    public string OriginalSql { get; set; } = string.Empty;
    public SqlError Error { get; set; } = new();
    public string CorrectedSql { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}