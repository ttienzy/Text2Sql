namespace TextToSqlAgent.Core.Models.DbExplorer;

/// <summary>
/// On-demand detailed analysis for a single table
/// </summary>
public class TableDetailAnalysis
{
    /// <summary>
    /// Table name
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Column interpretations (Vietnamese + English meanings)
    /// </summary>
    public Dictionary<string, ColumnMeaning> ColumnInterpretations { get; set; } = new();

    /// <summary>
    /// Implicit relationships detected (metadata-only)
    /// </summary>
    public List<ImplicitRelationship> ImplicitRelationships { get; set; } = new();

    /// <summary>
    /// Table-specific health issues
    /// </summary>
    public List<HealthIssue> HealthIssues { get; set; } = new();

    /// <summary>
    /// When this analysis was performed
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Column meaning interpretation (Vietnamese + English)
/// </summary>
public class ColumnMeaning
{
    /// <summary>
    /// Vietnamese meaning
    /// </summary>
    public string Vietnamese { get; set; } = string.Empty;

    /// <summary>
    /// English translation
    /// </summary>
    public string English { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Implicit foreign key relationship (detected without explicit FK constraint)
/// </summary>
public class ImplicitRelationship
{
    /// <summary>
    /// Child table name
    /// </summary>
    public string FromTable { get; set; } = string.Empty;

    /// <summary>
    /// Child column name
    /// </summary>
    public string FromColumn { get; set; } = string.Empty;

    /// <summary>
    /// Parent table name
    /// </summary>
    public string ToTable { get; set; } = string.Empty;

    /// <summary>
    /// Parent column name
    /// </summary>
    public string ToColumn { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Detection method (naming, metadata, llm)
    /// </summary>
    public string DetectionMethod { get; set; } = string.Empty;

    /// <summary>
    /// Reason for detection
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this requires data validation (optional)
    /// </summary>
    public bool RequiresDataValidation { get; set; }
}
