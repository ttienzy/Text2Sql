namespace TextToSqlAgent.Core.Models;

public class RetrievedSchemaContext
{
    public List<TableInfo> RelevantTables { get; set; } = new();
    public List<RelationshipInfo> RelevantRelationships { get; set; } = new();
    public Dictionary<string, List<ColumnInfo>> TableColumns { get; set; } = new();
    public List<SchemaMatch> Matches { get; set; } = new();
    public List<SchemaMatch> SchemaMatches { get; set; } = new(); // Alias for backward compatibility
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