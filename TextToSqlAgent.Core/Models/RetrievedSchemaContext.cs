namespace TextToSqlAgent.Core.Models;

public class RetrievedSchemaContext
{
    public List<TableInfo> RelevantTables { get; set; } = new();
    public List<RelationshipInfo> RelevantRelationships { get; set; } = new();
    public Dictionary<string, List<ColumnInfo>> TableColumns { get; set; } = new();
    public List<SchemaMatch> Matches { get; set; } = new();
    public List<SchemaMatch> SchemaMatches { get; set; } = new(); // Alias for backward compatibility

    /// <summary>
    /// List of retrieval strategies that were successfully used (e.g., "vector", "keyword", "graph").
    /// </summary>
    public List<string> RetrievalStrategies { get; set; } = new();

    /// <summary>
    /// Dictionary mapping element identifiers to their combined relevance scores.
    /// </summary>
    public Dictionary<string, float> ElementScores { get; set; } = new();

    /// <summary>
    /// Error message when all retrieval strategies fail or encounter errors.
    /// Null or empty when retrieval is successful.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

public class SchemaMatch
{
    public string Type { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty; // Alias for Type
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string ElementName { get; set; } = string.Empty; // Element name (table or column)
    public double Score { get; set; }
    public string Content { get; set; } = string.Empty;
}