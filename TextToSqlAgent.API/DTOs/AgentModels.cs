namespace TextToSqlAgent.API.DTOs;

public class QueryRequest
{
    public string Question { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
}

public class QueryResponse
{
    public bool Success { get; set; }
    public string? SqlGenerated { get; set; }
    public object? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string>? ProcessingSteps { get; set; }
    public string? Answer { get; set; }
}
