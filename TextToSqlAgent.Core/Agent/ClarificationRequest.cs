using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Types of clarification requests
/// </summary>
public enum ClarificationType
{
    /// <summary>User question is ambiguous and needs clarification</summary>
    ambiguous_question,

    /// <summary>DML query (UPDATE/DELETE) requires confirmation</summary>
    dml_confirmation,

    /// <summary>Query result is unclear, needs user interpretation</summary>
    result_interpretation,

    /// <summary>Missing required parameters</summary>
    missing_parameters,

    /// <summary>Timeout waiting for user response</summary>
    timeout
}

/// <summary>
/// Represents a clarification request that pauses agent execution
/// </summary>
public class ClarificationRequest
{
    /// <summary>Unique session identifier for tracking this clarification</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Type of clarification needed</summary>
    [JsonPropertyName("type")]
    public ClarificationType Type { get; set; }

    /// <summary>The question or prompt to ask the user</summary>
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>Available options for the user to choose from (if any)</summary>
    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    /// <summary>Timeout in seconds before clarification request expires</summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 300; // Default 5 minutes

    /// <summary>Timestamp when clarification was requested (UTC)</summary>
    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The original user question that triggered this clarification</summary>
    [JsonPropertyName("originalQuestion")]
    public string? OriginalQuestion { get; set; }

    /// <summary>SQL query that needs confirmation (for DML confirmation)</summary>
    [JsonPropertyName("sqlQuery")]
    public string? SqlQuery { get; set; }

    /// <summary>Additional context data</summary>
    [JsonPropertyName("context")]
    public Dictionary<string, object>? Context { get; set; }
}

/// <summary>
/// Request body for answering a clarification
/// </summary>
public class ClarificationAnswer
{
    /// <summary>The user's answer (free text)</summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    /// <summary>Selected option from available options (if applicable)</summary>
    [JsonPropertyName("selectedOption")]
    public string? SelectedOption { get; set; }

    /// <summary>Whether user confirmed DML execution</summary>
    [JsonPropertyName("confirmed")]
    public bool? Confirmed { get; set; }
}

/// <summary>
/// Session state stored in Redis for resuming after clarification
/// </summary>
public class AgentSessionState
{
    /// <summary>Session identifier</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>User ID who owns this session</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Conversation ID if any</summary>
    public string? ConversationId { get; set; }

    /// <summary>Original question from user</summary>
    public string OriginalQuestion { get; set; } = string.Empty;

    /// <summary>Current agent state (serialized)</summary>
    public string? AgentStateJson { get; set; }

    /// <summary>Working memory at point of clarification</summary>
    public Dictionary<string, object>? WorkingMemory { get; set; }

    /// <summary>Steps executed so far</summary>
    public List<AgentStep>? Steps { get; set; }

    /// <summary>The pending SQL that triggered DML confirmation</summary>
    public string? PendingSql { get; set; }

    /// <summary>Clarification type that is pending</summary>
    public ClarificationType? PendingClarificationType { get; set; }

    /// <summary>Timestamp when session was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp when session expires</summary>
    public DateTime ExpiresAt { get; set; }
}