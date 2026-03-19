namespace TextToSqlAgent.Core.Models.DbExplorer;

/// <summary>
/// Report of schema changes detected between two schema snapshots
/// </summary>
public class SchemaChangeReport
{
    public List<TableChange> NewTables { get; set; } = [];
    public List<TableChange> DeletedTables { get; set; } = [];
    public List<TableChange> ModifiedTables { get; set; } = [];
    public DateTime ComparedAt { get; set; }
    public string OldFingerprint { get; set; } = string.Empty;
    public string NewFingerprint { get; set; } = string.Empty;
    public bool HasChanges => NewTables.Count > 0 || DeletedTables.Count > 0 || ModifiedTables.Count > 0;
}

/// <summary>
/// Represents a change to a table
/// </summary>
public class TableChange
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public ChangeType Type { get; set; }
    public List<ColumnChange> ColumnChanges { get; set; } = [];
    public List<IndexChange> IndexChanges { get; set; } = [];
    public string? OldDescription { get; set; }
    public string? NewDescription { get; set; }
}

/// <summary>
/// Represents a change to a column
/// </summary>
public class ColumnChange
{
    public string ColumnName { get; set; } = string.Empty;
    public ChangeType Type { get; set; }
    public string? OldDataType { get; set; }
    public string? NewDataType { get; set; }
    public bool? OldIsNullable { get; set; }
    public bool? NewIsNullable { get; set; }
    public int? OldMaxLength { get; set; }
    public int? NewMaxLength { get; set; }
}

/// <summary>
/// Represents a change to an index
/// </summary>
public class IndexChange
{
    public string IndexName { get; set; } = string.Empty;
    public ChangeType Type { get; set; }
    public List<string> Columns { get; set; } = [];
}

/// <summary>
/// Type of change detected
/// </summary>
public enum ChangeType
{
    Added,
    Removed,
    Modified
}
