namespace TextToSqlAgent.Core.Models.DbExplorer;

/// <summary>
/// Role classification for database tables
/// </summary>
public enum TableRole
{
    /// <summary>
    /// Master data - foundational, rarely changes (Products, Categories)
    /// </summary>
    Master,

    /// <summary>
    /// Transaction data - records business events (Orders, Payments)
    /// </summary>
    Transaction,

    /// <summary>
    /// Bridge/Junction table - connects many-to-many (OrderItems, UserRoles)
    /// </summary>
    Bridge,

    /// <summary>
    /// Configuration data - system settings (Settings, Permissions)
    /// </summary>
    Config,

    /// <summary>
    /// Log/Audit data - tracking history (AuditLogs, History)
    /// </summary>
    LogAudit,

    /// <summary>
    /// Unknown/Unclassified
    /// </summary>
    Unknown
}

/// <summary>
/// Table role assignment with description
/// </summary>
public class TableRoleInfo
{
    public string TableName { get; set; } = string.Empty;
    public TableRole Role { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
