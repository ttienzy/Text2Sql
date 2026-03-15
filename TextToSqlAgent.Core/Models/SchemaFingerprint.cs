namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Represents a fingerprint of a database schema structure for change detection.
/// Used to determine if schema has changed and requires re-indexing.
/// </summary>
public class SchemaFingerprint
{
    /// <summary>
    /// SHA256 hash of the schema structure (tables, columns, relationships).
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the fingerprint was computed.
    /// </summary>
    public DateTime ComputedAt { get; set; }

    /// <summary>
    /// Total number of tables in the schema.
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Total number of columns across all tables.
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// Total number of foreign key relationships.
    /// </summary>
    public int RelationshipCount { get; set; }

    /// <summary>
    /// List of all table names in the schema (sorted for consistency).
    /// </summary>
    public List<string> TableNames { get; set; } = new();
}
