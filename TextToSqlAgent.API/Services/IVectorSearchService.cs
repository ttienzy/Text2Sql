namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for vector-based schema search and retrieval
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Search for relevant database schema based on natural language query
    /// </summary>
    Task<SchemaSearchResult> SearchSchemaAsync(string query, string connectionId, int maxResults = 10);

    /// <summary>
    /// Index database schema for vector search
    /// </summary>
    Task<bool> IndexSchemaAsync(string connectionId);

    /// <summary>
    /// Get schema indexing status
    /// </summary>
    Task<SchemaIndexStatus> GetIndexStatusAsync(string connectionId);

    /// <summary>
    /// Refresh schema index for a connection
    /// </summary>
    Task<bool> RefreshIndexAsync(string connectionId);

    /// <summary>
    /// Search for similar queries based on vector similarity
    /// </summary>
    Task<IEnumerable<SimilarQuery>> FindSimilarQueriesAsync(string query, string userId, int maxResults = 5);

    /// <summary>
    /// Store a successful query for future similarity search
    /// </summary>
    Task StoreQueryAsync(string query, string sqlQuery, string userId, string connectionId);
}

/// <summary>
/// Result of schema search
/// </summary>
public class SchemaSearchResult
{
    public bool Success { get; set; }
    public List<SchemaMatch> Matches { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public TimeSpan SearchTime { get; set; }
}

/// <summary>
/// Schema match from vector search
/// </summary>
public class SchemaMatch
{
    public string TableName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string? DataType { get; set; }
    public string? Description { get; set; }
    public double Similarity { get; set; }
    public string MatchType { get; set; } = string.Empty; // "table", "column", "relationship"
}

/// <summary>
/// Schema indexing status
/// </summary>
public class SchemaIndexStatus
{
    public bool IsIndexed { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public bool IsIndexing { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Similar query result
/// </summary>
public class SimilarQuery
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
}