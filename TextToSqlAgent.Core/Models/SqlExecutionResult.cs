namespace TextToSqlAgent.Core.Models;

public class SqlExecutionResult
{
    public bool Success { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ErrorDetails { get; set; }
    public int RowCount => Rows.Count;
}