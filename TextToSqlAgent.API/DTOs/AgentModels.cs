namespace TextToSqlAgent.API.DTOs;

public class QueryRequest
{
    /// <summary>
    /// Natural language question to convert to SQL
    /// </summary>
    public string Question { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional conversation ID for context persistence
    /// </summary>
    public string? ConversationId { get; set; }
}

public class QueryResponse
{
    /// <summary>
    /// Whether the query was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// The generated SQL query
    /// </summary>
    public string? SqlGenerated { get; set; }
    
    /// <summary>
    /// Query result rows
    /// </summary>
    public object? Result { get; set; }
    
    /// <summary>
    /// Number of rows returned
    /// </summary>
    public int RowCount { get; set; }
    
    /// <summary>
    /// Error message if query failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Processing steps taken by the agent
    /// </summary>
    public List<string>? ProcessingSteps { get; set; }
    
    /// <summary>
    /// Natural language answer
    /// </summary>
    public string? Answer { get; set; }
    
    /// <summary>
    /// Whether SQL was auto-corrected
    /// </summary>
    public bool WasCorrected { get; set; }
    
    /// <summary>
    /// Number of correction attempts made
    /// </summary>
    public int CorrectionAttempts { get; set; }
}
