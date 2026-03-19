namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Maintains conversation history and context for multi-turn interactions
/// </summary>
public class ConversationContext
{
    /// <summary>
    /// Unique conversation ID
    /// </summary>
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Conversation history (last N turns)
    /// </summary>
    public List<ConversationTurn> History { get; set; } = new();

    /// <summary>
    /// Last mentioned tables
    /// </summary>
    public List<string> RecentTables { get; set; } = new();

    /// <summary>
    /// Last mentioned columns
    /// </summary>
    public List<string> RecentColumns { get; set; } = new();

    /// <summary>
    /// Last applied filters
    /// </summary>
    public List<FilterCondition> RecentFilters { get; set; } = new();

    /// <summary>
    /// Last SQL query generated
    /// </summary>
    public string? LastSqlQuery { get; set; }

    /// <summary>
    /// Last query result summary
    /// </summary>
    public string? LastResultSummary { get; set; }

    /// <summary>
    /// Conversation started at
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total turns in conversation
    /// </summary>
    public int TurnCount => History.Count;
}

/// <summary>
/// Single turn in conversation
/// </summary>
public class ConversationTurn
{
    /// <summary>
    /// Turn number (1-indexed)
    /// </summary>
    public int TurnNumber { get; set; }

    /// <summary>
    /// User's question
    /// </summary>
    public string UserQuestion { get; set; } = string.Empty;

    /// <summary>
    /// System's response
    /// </summary>
    public string SystemResponse { get; set; } = string.Empty;

    /// <summary>
    /// SQL query generated (if any)
    /// </summary>
    public string? SqlQuery { get; set; }

    /// <summary>
    /// Query intent
    /// </summary>
    public QueryIntent? Intent { get; set; }

    /// <summary>
    /// Target table
    /// </summary>
    public string? TargetTable { get; set; }

    /// <summary>
    /// Timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether query was successful
    /// </summary>
    public bool Success { get; set; }

    // ✅ NEW - Structured Context Snapshot

    /// <summary>
    /// All entities (tables) referenced in this turn
    /// </summary>
    public List<string> EntitiesReferenced { get; set; } = new();

    /// <summary>
    /// Primary entity being queried
    /// </summary>
    public string? PrimaryEntity { get; set; }

    /// <summary>
    /// Columns mentioned/used (alias → actual column name)
    /// </summary>
    public Dictionary<string, string> Columns { get; set; } = new();

    /// <summary>
    /// High-level query intent (LIST, COUNT, FILTER, etc.)
    /// </summary>
    public string? QueryIntentType { get; set; }
}
