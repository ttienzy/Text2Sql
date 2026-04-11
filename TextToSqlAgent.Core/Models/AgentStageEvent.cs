namespace TextToSqlAgent.Core.Models;

/// <summary>
/// LARGE-1: Represents a stage event emitted during agent processing.
/// Used by IProgress<AgentStageEvent> to stream SSE updates to the client.
/// </summary>
public class AgentStageEvent
{
    /// <summary>The current pipeline stage.</summary>
    public AgentStage Stage { get; init; }

    /// <summary>Human-readable description of what's happening.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>0.0 – 1.0 progress within the overall pipeline.</summary>
    public double Progress { get; init; }

    /// <summary>Optional detail (e.g. table names being retrieved).</summary>
    public string? Detail { get; init; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Pipeline stages for SSE streaming.
/// Matches the actual processing steps inside EnhancedAgentOrchestrator.
/// </summary>
public enum AgentStage
{
    /// <summary>Validating and normalizing the user question.</summary>
    VALIDATING,

    /// <summary>Classifying intent (SELECT/WRITE/DDL/FORBIDDEN).</summary>
    CLASSIFYING,

    /// <summary>Agent is planning the next step.</summary>
    AGENT_THINKING,

    /// <summary>Agent is executing a tool.</summary>
    AGENT_ACTION,

    /// <summary>Loading and retrieving relevant schema from Qdrant.</summary>
    SCHEMA_RETRIEVAL,

    /// <summary>Generating SQL via LLM.</summary>
    SQL_GENERATION,

    /// <summary>Validating generated SQL for safety.</summary>
    SQL_VALIDATION,

    /// <summary>SQL preview generated — showing to user before confirmation (DML/DDL).</summary>
    SQL_PREVIEW,

    /// <summary>Waiting for user confirmation before executing DML/DDL operation.</summary>
    AWAITING_CONFIRM,

    /// <summary>Executing SQL against the database.</summary>
    EXECUTING,

    /// <summary>LLM is self-correcting a failed query (retry loop).</summary>
    CORRECTING,

    /// <summary>Building the final response.</summary>
    BUILDING_RESPONSE,

    /// <summary>Processing complete — final result attached.</summary>
    COMPLETED,

    /// <summary>Operation was blocked by safety policy (FORBIDDEN).</summary>
    BLOCKED,

    /// <summary>An error occurred.</summary>
    ERROR
}
