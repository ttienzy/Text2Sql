using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.API.DTOs;

/// <summary>
/// Database overview response
/// </summary>
public class DatabaseOverviewResponse
{
    public string Domain { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public long TotalRows { get; set; }
    public List<ModuleDto> Modules { get; set; } = new();
    public int IssueCount { get; set; }
    public DateTime ScannedAt { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// Module DTO
/// </summary>
public class ModuleDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tables { get; set; } = new();
}

/// <summary>
/// Table list response
/// </summary>
public class TableListResponse
{
    public List<TableSummaryDto> Tables { get; set; } = new();
}

/// <summary>
/// Table summary DTO
/// </summary>
public class TableSummaryDto
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public TableRole Role { get; set; }
    public string? Module { get; set; }
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public int ForeignKeyCount { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Table detail response
/// </summary>
public class TableDetailResponse
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public TableRole Role { get; set; }
    public string? Module { get; set; }
    public long RowCount { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<ColumnDetailDto> Columns { get; set; } = new();
    public List<RelationshipDto> Relationships { get; set; } = new();
    public List<IndexDto> Indexes { get; set; } = new();
}

/// <summary>
/// Column detail DTO
/// </summary>
public class ColumnDetailDto
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public int? MaxLength { get; set; }
    public ColumnStatsDto? Statistics { get; set; }
}

/// <summary>
/// Column statistics DTO
/// </summary>
public class ColumnStatsDto
{
    public double NullRate { get; set; }
    public long DistinctCount { get; set; }
    public object? MinValue { get; set; }
    public object? MaxValue { get; set; }
    public double? AvgValue { get; set; }
}

/// <summary>
/// Relationship DTO
/// </summary>
public class RelationshipDto
{
    public string Direction { get; set; } = string.Empty; // "incoming" or "outgoing"
    public string RelatedTable { get; set; } = string.Empty;
    public string ViaColumn { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Index DTO
/// </summary>
public class IndexDto
{
    public string IndexName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
}

/// <summary>
/// Health check response
/// </summary>
public class HealthCheckResponse
{
    public int TotalIssues { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public List<HealthIssueDto> Issues { get; set; } = new();
}

/// <summary>
/// Health issue DTO
/// </summary>
public class HealthIssueDto
{
    public string Severity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string? Column { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

/// <summary>
/// Graph data response
/// </summary>
public class GraphDataResponse
{
    public List<GraphNodeDto> Nodes { get; set; } = new();
    public List<GraphEdgeDto> Edges { get; set; } = new();
}

/// <summary>
/// Graph node DTO
/// </summary>
public class GraphNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string? Module { get; set; }
    public List<string> PrimaryKeys { get; set; } = new();
    public List<string> ForeignKeys { get; set; } = new();
    public List<GraphColumnDto> Columns { get; set; } = new();
}

/// <summary>
/// Graph column DTO
/// </summary>
public class GraphColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsNullable { get; set; }
}

/// <summary>
/// Graph edge DTO
/// </summary>
public class GraphEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Via { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
}
