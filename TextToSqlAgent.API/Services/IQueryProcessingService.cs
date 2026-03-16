using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for processing natural language queries to SQL
/// </summary>
public interface IQueryProcessingService
{
    /// <summary>
    /// Process a natural language query and return SQL with results
    /// </summary>
    Task<QueryProcessingResult> ProcessQueryAsync(string question, string connectionId, string userId);

    /// <summary>
    /// Process a query asynchronously (background job)
    /// </summary>
    Task<string> ProcessQueryAsyncJob(string question, string connectionId, string userId);

    /// <summary>
    /// Get processing status for an async job
    /// </summary>
    Task<AgentJob?> GetJobStatusAsync(string jobId, string userId);

    /// <summary>
    /// Validate a SQL query without executing it
    /// </summary>
    Task<QueryValidationResult> ValidateQueryAsync(string sqlQuery, string connectionId, string userId);

    /// <summary>
    /// Execute a pre-validated SQL query
    /// </summary>
    Task<QueryExecutionResult> ExecuteQueryAsync(string sqlQuery, string connectionId, string userId);

    /// <summary>
    /// Explain a SQL query in natural language
    /// </summary>
    Task<string> ExplainQueryAsync(string sqlQuery, string connectionId, string userId);
}

/// <summary>
/// Result of query processing
/// </summary>
public class QueryProcessingResult
{
    public bool Success { get; set; }
    public string? SqlQuery { get; set; }
    public string? Results { get; set; }
    public int? RowCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Explanation { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? Cost { get; set; }
    public string? Model { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// Result of query validation
/// </summary>
public class QueryValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    public bool IsSafe { get; set; }
    public string? SecurityIssue { get; set; }
}

/// <summary>
/// Result of query execution
/// </summary>
public class QueryExecutionResult
{
    public bool Success { get; set; }
    public string? Results { get; set; }
    public int? RowCount { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}