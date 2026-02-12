namespace TextToSqlAgent.Core.Models;

public class DatabaseSchema
{
    public List<TableInfo> Tables { get; set; } = new();
    public List<RelationshipInfo> Relationships { get; set; } = new();
    public DateTime ScannedAt { get; set; }
}

public class TableInfo
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<string> PrimaryKeys { get; set; } = new();
}

public class ColumnInfo
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
}

public class RelationshipInfo
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
}