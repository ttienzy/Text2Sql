namespace TextToSqlAgent.Core.Models;

public class SchemaDocument
{
    public string Id { get; set; } = string.Empty;
    public SchemaDocumentType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public enum SchemaDocumentType
{
    Table,
    Column,
    Relationship
}