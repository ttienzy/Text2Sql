namespace TextToSqlAgent.Core.Models;

public class RetrievedSchemaContext
{
    public List<TableInfo> RelevantTables { get; set; } = new();
    public List<RelationshipInfo> RelevantRelationships { get; set; } = new();
    public Dictionary<string, List<ColumnInfo>> TableColumns { get; set; } = new();
    public List<SchemaMatch> Matches { get; set; } = new();
}

public class SchemaMatch
{
    public string Type { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public double Score { get; set; }
    public string Content { get; set; } = string.Empty;
}