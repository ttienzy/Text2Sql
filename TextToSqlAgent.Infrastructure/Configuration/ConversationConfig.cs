namespace TextToSqlAgent.Infrastructure.Configuration;

/// <summary>
/// Configuration for conversation-aware features
/// </summary>
public class ConversationConfig
{
    /// <summary>
    /// Maximum number of messages to include in conversation history
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 20;

    /// <summary>
    /// Time-to-live for conversation memory in hours
    /// </summary>
    public int ConversationMemoryTTLHours { get; set; } = 24;

    /// <summary>
    /// Enable automatic follow-up question detection
    /// </summary>
    public bool EnableFollowUpDetection { get; set; } = true;

    /// <summary>
    /// Maximum number of conversation turns before requiring reset
    /// </summary>
    public int MaxConversationTurns { get; set; } = 50;

    /// <summary>
    /// Enable conversation analytics and metrics
    /// </summary>
    public bool EnableConversationAnalytics { get; set; } = true;

    /// <summary>
    /// Enable conversation context in agent reasoning
    /// </summary>
    public bool EnableConversationContext { get; set; } = true;

    /// <summary>
    /// Minimum similarity threshold for follow-up detection
    /// </summary>
    public double FollowUpSimilarityThreshold { get; set; } = 0.7;

    /// <summary>
    /// Enable conversation memory persistence across sessions
    /// </summary>
    public bool EnableMemoryPersistence { get; set; } = true;

    /// <summary>
    /// Maximum size of conversation memory in MB
    /// </summary>
    public int MaxMemorySizeMB { get; set; } = 100;
}