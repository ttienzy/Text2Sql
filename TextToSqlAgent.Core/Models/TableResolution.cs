namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Result of semantic table resolution
/// </summary>
public class TableResolutionResult
{
    /// <summary>
    /// Whether resolution was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Resolved table name (exact match from database schema)
    /// </summary>
    public string? ResolvedTableName { get; set; }

    /// <summary>
    /// Original entity mention from user question
    /// </summary>
    public string? OriginalMention { get; set; }

    /// <summary>
    /// Confidence score (0.0 to 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Alternative table candidates if confidence is low
    /// </summary>
    public List<TableCandidate> Alternatives { get; set; } = new();

    /// <summary>
    /// Reasoning for the resolution
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// Error message if resolution failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the resolution is ambiguous (multiple strong candidates)
    /// </summary>
    public bool IsAmbiguous => Alternatives.Count > 1 && Alternatives.Any(a => a.Confidence > 0.7);
}

/// <summary>
/// A candidate table for semantic resolution
/// </summary>
public class TableCandidate
{
    /// <summary>
    /// Table name from database schema
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score for this candidate (0.0 to 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Reason why this table was selected
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Semantic similarity score
    /// </summary>
    public double SemanticSimilarity { get; set; }
}
