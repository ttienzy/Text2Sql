namespace TextToSqlAgent.Core.Models.DbExplorer;

public class DataQualityReport
{
    public double OverallScore { get; set; }
    public List<QualityIssue> Issues { get; set; } = [];
    public Dictionary<string, int> IssuesByCategory { get; set; } = [];
    public Dictionary<string, int> IssuesBySeverity { get; set; } = [];
    public DateTime GeneratedAt { get; set; }
}

public class QualityIssue
{
    public string TableName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "null_rate", "missing_index", "orphaned"
    public IssueSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public Dictionary<string, object> Metrics { get; set; } = [];
}
