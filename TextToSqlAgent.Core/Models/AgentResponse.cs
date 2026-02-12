namespace TextToSqlAgent.Core.Models;

public class AgentResponse
{
    public bool Success { get; set; }
    public string Answer { get; set; } = string.Empty;
    public string? SqlGenerated { get; set; }
    public SqlExecutionResult? QueryResult { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ProcessingSteps { get; set; } = new();

    // NEW: Self-correction tracking
    public List<CorrectionAttempt> CorrectionHistory { get; set; } = new();
    public bool WasCorrected { get; set; }
    public int CorrectionAttempts { get; set; }
}