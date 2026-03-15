namespace TextToSqlAgent.Application.Pipelines;

using TextToSqlAgent.Core.Models;
using QueryComplexity = TextToSqlAgent.Application.Routing.QueryComplexity;

/// <summary>
/// Result model for query pipeline execution
/// Contains generated SQL, data, and formatted answer
/// </summary>
public class QueryResult
{
    /// <summary>
    /// Whether the query was executed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Generated SQL query
    /// </summary>
    public string? SqlGenerated { get; set; }

    /// <summary>
    /// Raw SQL execution result
    /// </summary>
    public SqlExecutionResult? QueryResultData { get; set; }

    /// <summary>
    /// Formatted natural language answer
    /// </summary>
    public string? FormattedAnswer { get; set; }

    /// <summary>
    /// Whether the query was escalated to a higher complexity pipeline
    /// </summary>
    public bool WasEscalated { get; set; }

    /// <summary>
    /// Reason for escalation if applicable
    /// </summary>
    public string? EscalationReason { get; set; }

    /// <summary>
    /// Complexity level this query was handled at
    /// </summary>
    public Application.Routing.QueryComplexity Complexity { get; set; }

    /// <summary>
    /// Number of LLM calls made during execution
    /// </summary>
    public int LlmCalls { get; set; }

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Processing steps taken
    /// </summary>
    public List<string> ProcessingSteps { get; set; } = new();
}
