namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Represents a chat session/conversation between a user and the AI agent
/// </summary>
public class Conversation
{
    /// <summary>
    /// Unique identifier for the conversation
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Foreign key to the user who owns this conversation
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the database connection used in this conversation (REQUIRED)
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// User-defined title for the conversation
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Whether the conversation is archived (hidden from list but still accessible)
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Timestamp when the conversation was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the conversation was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the conversation was last active (for cleanup)
    /// </summary>
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON-serialized conversation context for LLM
    /// Contains: mentioned tables, aliases, last SQL, user preferences
    /// </summary>
    public string? ContextJson { get; set; }

    /// <summary>
    /// Navigation property to the user who owns this conversation
    /// </summary>
    public virtual ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Navigation property to the database connection
    /// </summary>
    public virtual Connection Connection { get; set; } = null!;

    /// <summary>
    /// Navigation property to messages in this conversation
    /// </summary>
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}