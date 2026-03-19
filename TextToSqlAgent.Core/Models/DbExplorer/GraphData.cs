namespace TextToSqlAgent.Core.Models.DbExplorer;

/// <summary>
/// Graph data for ER diagram visualization
/// </summary>
public class GraphData
{
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();
}

/// <summary>
/// Graph node representing a table
/// </summary>
public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public TableRole Role { get; set; }
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string? Module { get; set; }
    public List<string> PrimaryKeys { get; set; } = new();
    public List<string> ForeignKeys { get; set; } = new();
    public List<GraphColumn> Columns { get; set; } = new();

    /// <summary>
    /// Position for layout (optional, can be calculated by frontend)
    /// </summary>
    public Position? Position { get; set; }
}

/// <summary>
/// Column info for graph node
/// </summary>
public class GraphColumn
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsNullable { get; set; }
}

/// <summary>
/// Graph edge representing a relationship
/// </summary>
public class GraphEdge
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Via { get; set; } = string.Empty;
    public RelationshipType Type { get; set; }
    public RelationshipStrength Strength { get; set; }
}

/// <summary>
/// Node position for graph layout
/// </summary>
public class Position
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// Relationship type
/// </summary>
public enum RelationshipType
{
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

/// <summary>
/// Relationship strength based on FK constraints
/// </summary>
public enum RelationshipStrength
{
    /// <summary>
    /// NOT NULL FK with cascade delete
    /// </summary>
    Tight,

    /// <summary>
    /// NOT NULL FK without cascade
    /// </summary>
    Moderate,

    /// <summary>
    /// Nullable FK
    /// </summary>
    Loose
}
