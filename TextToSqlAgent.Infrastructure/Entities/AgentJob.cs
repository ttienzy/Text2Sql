namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Represents an async job for processing agent queries in the background
/// </summary>
public class AgentJob
{
    /// <summary>
    /// Unique identifier for the job
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// ID of the user who created this job
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The question/query to be processed by the agent
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// ID of the database connection to use
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the job
    /// </summary>
    public JobStatus Status { get; set; } = JobStatus.Queued;

    /// <summary>
    /// The generated SQL query (if successful)
    /// </summary>
    public string? SqlQuery { get; set; }

    /// <summary>
    /// Query results as JSON string
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Number of rows returned
    /// </summary>
    public int? RowCount { get; set; }

    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Explanation of the SQL in Vietnamese
    /// </summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// Number of input tokens used
    /// </summary>
    public int? InputTokens { get; set; }

    /// <summary>
    /// Number of output tokens used
    /// </summary>
    public int? OutputTokens { get; set; }

    /// <summary>
    /// Total tokens used
    /// </summary>
    public int? TotalTokens { get; set; }

    /// <summary>
    /// Cost in USD
    /// </summary>
    public decimal? Cost { get; set; }

    /// <summary>
    /// AI model used
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Hangfire job ID (for tracking and cancellation)
    /// </summary>
    public string? HangfireJobId { get; set; }

    /// <summary>
    /// Timestamp when the job was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the job started processing
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when the job completed (success or failure)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Time taken to process the job (in seconds)
    /// </summary>
    public int? ProcessingTimeSeconds { get; set; }
}

/// <summary>
/// Job status enumeration
/// </summary>
public enum JobStatus
{
    /// <summary>
    /// Job is queued and waiting to be processed
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Job is currently being processed
    /// </summary>
    Running = 1,

    /// <summary>
    /// Job completed successfully
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Job failed with an error
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Job was cancelled by the user
    /// </summary>
    Cancelled = 4
}
