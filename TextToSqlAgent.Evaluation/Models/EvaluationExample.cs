namespace TextToSqlAgent.Evaluation.Models;

/// <summary>
/// Represents a single test case for evaluation
/// </summary>
public class EvaluationExample
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string DatabaseId { get; set; } = string.Empty;
    public string GroundTruthSql { get; set; } = string.Empty;
    public List<Dictionary<string, object>>? ExpectedResults { get; set; }
    public string Difficulty { get; set; } = "Easy"; // Easy, Medium, Hard, Extra
    public List<string> RequiredTables { get; set; } = new();
    public List<string> RequiredColumns { get; set; } = new();
}
