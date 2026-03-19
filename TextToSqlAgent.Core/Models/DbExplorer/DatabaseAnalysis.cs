namespace TextToSqlAgent.Core.Models.DbExplorer;

/// <summary>
/// Complete database analysis result from AI
/// </summary>
public class DatabaseAnalysis
{
    /// <summary>
    /// Domain classification (E-commerce, CRM, ERP, etc.)
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Summary description of the database
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Logical modules/groups of tables
    /// </summary>
    public List<DatabaseModule> Modules { get; set; } = new();

    /// <summary>
    /// Role assignments for each table
    /// </summary>
    public Dictionary<string, TableRoleInfo> TableRoles { get; set; } = new();

    /// <summary>
    /// Health issues detected
    /// </summary>
    public List<HealthIssue> HealthIssues { get; set; } = new();

    /// <summary>
    /// When this analysis was performed
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Logical module/group of related tables
/// </summary>
public class DatabaseModule
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tables { get; set; } = new();
}

/// <summary>
/// Health issue detected in schema
/// </summary>
public class HealthIssue
{
    public IssueSeverity Severity { get; set; }
    public IssueType Type { get; set; }
    public string Table { get; set; } = string.Empty;
    public string? Column { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

/// <summary>
/// Severity level of health issue
/// </summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Type of health issue
/// </summary>
public enum IssueType
{
    MissingIndex,
    OrphanTable,
    InconsistentNaming,
    MissingPrimaryKey,
    NullableRequired,
    UnusedTable,
    CircularDependency
}
