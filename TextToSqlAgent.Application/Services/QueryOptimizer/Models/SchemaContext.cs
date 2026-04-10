using System.Collections.Generic;

namespace TextToSqlAgent.Application.Services.QueryOptimizer.Models;

/// <summary>
/// Schema context for query optimization
/// </summary>
public class SchemaContext
{
    public List<TableSchema> Tables { get; set; } = new();
    public List<string> MissingTables { get; set; } = new();
    public List<string> EnrichmentWarnings { get; set; } = new();
}

/// <summary>
/// Table schema information
/// </summary>
public class TableSchema
{
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<IndexInfo> Indexes { get; set; } = new();
    public List<ForeignKeyInfo> ForeignKeys { get; set; } = new();
}

/// <summary>
/// Column information
/// </summary>
public class ColumnInfo
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool HasIndex { get; set; }
}

/// <summary>
/// Index information
/// </summary>
public class IndexInfo
{
    public string IndexName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public bool IsClustered { get; set; }
}

/// <summary>
/// Foreign key information
/// </summary>
public class ForeignKeyInfo
{
    public string ForeignKeyName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
}
