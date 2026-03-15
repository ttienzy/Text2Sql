namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Represents a schema element with individual scores from different retrieval strategies.
/// Used in hybrid retrieval to track and combine scores from vector, keyword, and graph search.
/// </summary>
public class ScoredSchemaElement
{
    /// <summary>
    /// The schema element (can be TableInfo, ColumnInfo, RelationshipInfo, or SchemaMatch).
    /// </summary>
    public object Element { get; set; } = null!;

    /// <summary>
    /// Score from vector similarity search (0.0 to 1.0).
    /// </summary>
    public float VectorScore { get; set; }

    /// <summary>
    /// Score from keyword matching (0.0 to 1.0).
    /// </summary>
    public float KeywordScore { get; set; }

    /// <summary>
    /// Score from schema graph traversal (0.0 to 1.0).
    /// </summary>
    public float GraphScore { get; set; }

    /// <summary>
    /// Combined weighted score from all retrieval strategies.
    /// </summary>
    public float CombinedScore { get; set; }
}
