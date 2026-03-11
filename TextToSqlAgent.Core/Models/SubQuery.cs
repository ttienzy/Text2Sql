namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Represents a decomposed sub-query
/// </summary>
public class SubQuery
{
    public int Step { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<int> Dependencies { get; set; } = new(); // Steps this depends on
    public string? GeneratedSql { get; set; }
    public object? Result { get; set; }
    public bool Executed { get; set; }
}

/// <summary>
/// Result of query decomposition
/// </summary>
public class DecomposedQuery
{
    public bool IsComplex { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public List<SubQuery> SubQueries { get; set; } = new();

    public bool RequiresDecomposition => IsComplex && SubQueries.Count > 1;
}
