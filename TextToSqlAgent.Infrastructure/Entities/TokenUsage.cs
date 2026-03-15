namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Tracks API token consumption for billing and monitoring purposes
/// </summary>
public class TokenUsage
{
    /// <summary>
    /// Unique identifier for the token usage record
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Foreign key to the user who consumed the tokens
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the conversation (optional, for detailed tracking)
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Foreign key to the connection used (optional)
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Number of input tokens consumed
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// Number of output tokens generated
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// Total tokens consumed (Input + Output)
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// AI model used for this request
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Cost incurred for this request in USD
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// Timestamp when the token usage was recorded
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property to the user who consumed the tokens
    /// </summary>
    public virtual ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Navigation property to the conversation (if any)
    /// </summary>
    public virtual Conversation? Conversation { get; set; }

    /// <summary>
    /// Navigation property to the connection (if any)
    /// </summary>
    public virtual Connection? Connection { get; set; }
}