namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Paginated query result for lazy loading
/// </summary>
public class PaginatedQueryResult
{
    /// <summary>
    /// Current page of rows
    /// </summary>
    public List<Dictionary<string, object>> Rows { get; set; } = new();

    /// <summary>
    /// Total number of rows available
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// Column names
    /// </summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Number of rows per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalRows / PageSize);

    /// <summary>
    /// Whether there are more pages available
    /// </summary>
    public bool HasMore => CurrentPage < TotalPages;

    /// <summary>
    /// Unique identifier for cached result (for pagination)
    /// </summary>
    public string? ResultId { get; set; }

    /// <summary>
    /// Timestamp when result was cached
    /// </summary>
    public DateTime CachedAt { get; set; }
}
