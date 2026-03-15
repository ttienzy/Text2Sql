namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Represents the result of a database connection operation, including schema indexing status.
/// </summary>
public class ConnectionResult
{
    /// <summary>
    /// Indicates whether the connection was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Indicates whether schema indexing was performed during this connection.
    /// </summary>
    public bool IndexingPerformed { get; set; }

    /// <summary>
    /// Number of vector points indexed (0 if indexing was skipped).
    /// </summary>
    public int PointsIndexed { get; set; }

    /// <summary>
    /// Duration of the indexing operation.
    /// </summary>
    public TimeSpan IndexingDuration { get; set; }

    /// <summary>
    /// Error message if the connection or indexing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
