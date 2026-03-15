namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Result from SQL generation including the SQL query and suggested follow-up queries
/// </summary>
public class SqlGenerationResult
{
    /// <summary>
    /// The generated SQL query
    /// </summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>
    /// List of suggested natural language follow-up queries
    /// </summary>
    public List<string> SuggestedQueries { get; set; } = new();

    /// <summary>
    /// Optional reasoning or explanation for debugging
    /// </summary>
    public string? Reasoning { get; set; }
}