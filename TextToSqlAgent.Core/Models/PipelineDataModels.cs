using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Marker interface for pipeline-specific data
/// Enables type-safe polymorphic serialization
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(QueryPipelineData), "query")]
[JsonDerivedType(typeof(WritePipelineData), "write")]
[JsonDerivedType(typeof(DdlPipelineData), "ddl")]
[JsonDerivedType(typeof(ForbiddenPipelineData), "forbidden")]
[JsonDerivedType(typeof(RejectionPipelineData), "reject")]
public interface IPipelineData { }

/// <summary>
/// Data returned by QUERY pipeline
/// </summary>
public class QueryPipelineData : IPipelineData
{
    /// <summary>
    /// Natural language answer to the user's question
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// SQL query execution result (paginated for large results)
    /// </summary>
    public SqlExecutionResult? QueryResult { get; set; }

    /// <summary>
    /// Natural language explanation of the SQL query
    /// </summary>
    public string? QueryExplanation { get; set; }

    /// <summary>
    /// AI-generated follow-up query suggestions
    /// </summary>
    public List<string> SuggestedQueries { get; set; } = new();

    /// <summary>
    /// Entities referenced in conversation context
    /// </summary>
    public List<string> ContextEntities { get; set; } = new();

    /// <summary>
    /// Primary entity being queried
    /// </summary>
    public string? PrimaryEntity { get; set; }

    /// <summary>
    /// Whether pronouns were detected and resolved
    /// </summary>
    public bool PronounsResolved { get; set; }

    /// <summary>
    /// Pagination metadata for large result sets
    /// </summary>
    public PaginationMetadata? Pagination { get; set; }

    /// <summary>
    /// Base64 encoded PNG chart visualization of the data
    /// </summary>
    public string? ChartImageBase64 { get; set; }

    /// <summary>
    /// The type of chart generated (bar, line, pie, etc.)
    /// </summary>
    public string? ChartType { get; set; }

    /// <summary>
    /// Result ID for fetching additional pages (lazy loading)
    /// </summary>
    public string? ResultId { get; set; }

    /// <summary>
    /// Whether this result has more pages available
    /// </summary>
    public bool HasMore { get; set; }
}

/// <summary>
/// Data returned by WRITE pipeline (preview or execution result)
/// </summary>
public class WritePipelineData : IPipelineData
{
    /// <summary>
    /// Write operation preview (before execution)
    /// </summary>
    public WriteOperationPreview? Preview { get; set; }

    /// <summary>
    /// Write operation result (after execution)
    /// </summary>
    public WriteOperationResult? Result { get; set; }

    /// <summary>
    /// Approval ID for async approval queue (new UX pattern)
    /// </summary>
    public string? ApprovalId { get; set; }
}

/// <summary>
/// Data returned by DDL pipeline (preview or execution result)
/// </summary>
public class DdlPipelineData : IPipelineData
{
    /// <summary>
    /// DDL operation preview with impact analysis (before execution)
    /// </summary>
    public DDLOperationPreview? Preview { get; set; }

    /// <summary>
    /// DDL operation result (after execution)
    /// </summary>
    public DDLOperationResult? Result { get; set; }
}

/// <summary>
/// Data returned by FORBIDDEN pipeline
/// </summary>
public class ForbiddenPipelineData : IPipelineData
{
    /// <summary>
    /// Forbidden operation rejection result with safe alternatives
    /// </summary>
    public ForbiddenOperationResult Result { get; set; } = null!;
}

/// <summary>
/// Data returned by REJECT pipeline (off-topic or unknown intent)
/// </summary>
public class RejectionPipelineData : IPipelineData
{
    /// <summary>
    /// Reason for rejection
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Detected language (en/vi)
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Original intent that was rejected
    /// </summary>
    public IntentCategory RejectedIntent { get; set; }
}

/// <summary>
/// Pagination metadata for query results
/// </summary>
public class PaginationMetadata
{
    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// Total number of items available
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);

    /// <summary>
    /// Whether there are more pages available
    /// </summary>
    public bool HasNextPage => CurrentPage < TotalPages;

    /// <summary>
    /// Whether there are previous pages available
    /// </summary>
    public bool HasPreviousPage => CurrentPage > 1;
}
