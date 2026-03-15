namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Represents an individual message within a conversation
/// </summary>
public class Message
{
    /// <summary>
    /// Unique identifier for the message
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Foreign key to the conversation this message belongs to
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// Role of the message sender (user, assistant, system)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Content of the message (plain text or query)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Generated SQL query (for assistant messages with query results)
    /// </summary>
    public string? SqlQuery { get; set; }

    /// <summary>
    /// Query results as JSON string
    /// </summary>
    public string? Results { get; set; }

    /// <summary>
    /// Number of rows returned
    /// </summary>
    public int? RowCount { get; set; }

    /// <summary>
    /// Error message if query execution failed
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
    /// Timestamp when the message was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property to the conversation this message belongs to
    /// </summary>
    public virtual Conversation Conversation { get; set; } = null!;
}