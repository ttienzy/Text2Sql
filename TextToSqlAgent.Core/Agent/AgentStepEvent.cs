using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Event types for streaming agent step updates via Server-Sent Events (SSE)
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentStepEventType
{
    /// <summary>Agent has started processing</summary>
    agent_started,

    /// <summary>Agent selected a tool to use</summary>
    tool_selected,

    /// <summary>Tool is currently executing</summary>
    tool_executing,

    /// <summary>Tool execution completed with result</summary>
    tool_result,

    /// <summary>Agent is reflecting on the result</summary>
    reflecting,

    /// <summary>SQL query has been generated</summary>
    sql_generated,

    /// <summary>SQL query is being executed</summary>
    sql_executing,

    /// <summary>Agent completed processing with final answer</summary>
    completed,

    /// <summary>An error occurred during processing</summary>
    error,

    /// <summary>Agent needs clarification from user before proceeding</summary>
    needs_clarification,

    /// <summary>User confirmed DML execution</summary>
    dml_confirmed,

    /// <summary>User rejected DML execution</summary>
    dml_rejected
}

/// <summary>
/// Represents a single step event in the agent's execution stream
/// Used for Server-Sent Events (SSE) streaming to provide real-time updates to clients
/// </summary>
public class AgentStepEvent
{
    /// <summary>
    /// The type of event
    /// </summary>
    [JsonPropertyName("type")]
    public AgentStepEventType EventType { get; set; }

    /// <summary>
    /// Timestamp when the event occurred (UTC)
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current step number (1-based), null for start/complete events
    /// </summary>
    [JsonPropertyName("step")]
    public int? StepNumber { get; set; }

    /// <summary>
    /// Total number of steps taken so far
    /// </summary>
    [JsonPropertyName("totalSteps")]
    public int TotalSteps { get; set; }

    /// <summary>
    /// Maximum number of steps allowed
    /// </summary>
    [JsonPropertyName("maxSteps")]
    public int MaxSteps { get; set; }

    /// <summary>
    /// Agent's thought/reasoning at this step
    /// </summary>
    [JsonPropertyName("thought")]
    public string? Thought { get; set; }

    /// <summary>
    /// Plan for the current step
    /// </summary>
    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    /// <summary>
    /// Tool that was selected or is being executed
    /// </summary>
    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    /// <summary>
    /// Tool input/parameters
    /// </summary>
    [JsonPropertyName("toolInput")]
    public string? ToolInput { get; set; }

    /// <summary>
    /// Tool execution result (preview or full)
    /// </summary>
    [JsonPropertyName("toolResult")]
    public string? ToolResult { get; set; }

    /// <summary>
    /// Whether the tool execution was successful
    /// </summary>
    [JsonPropertyName("toolSuccess")]
    public bool? ToolSuccess { get; set; }

    /// <summary>
    /// Generated SQL query
    /// </summary>
    [JsonPropertyName("sqlGenerated")]
    public string? SqlGenerated { get; set; }

    /// <summary>
    /// SQL execution result preview
    /// </summary>
    [JsonPropertyName("sqlResult")]
    public object? SqlResult { get; set; }

    /// <summary>
    /// Number of rows returned from SQL execution
    /// </summary>
    [JsonPropertyName("rowCount")]
    public int? RowCount { get; set; }

    /// <summary>
    /// Final answer from the agent
    /// </summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    /// <summary>
    /// Error message if event type is error
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the error is recoverable (agent can continue)
    /// </summary>
    [JsonPropertyName("recoverable")]
    public bool Recoverable { get; set; }

    /// <summary>
    /// Additional metadata about the current state
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Creates an agent_started event
    /// </summary>
    public static AgentStepEvent AgentStarted(string question, int maxSteps)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.agent_started,
            MaxSteps = maxSteps,
            Metadata = new Dictionary<string, object>
            {
                ["question"] = question.Length > 100 ? question.Substring(0, 100) + "..." : question
            }
        };
    }

    /// <summary>
    /// Creates a tool_selected event
    /// </summary>
    public static AgentStepEvent ToolSelected(int stepNumber, string toolName, string? thought)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.tool_selected,
            StepNumber = stepNumber,
            ToolName = toolName,
            Thought = thought
        };
    }

    /// <summary>
    /// Creates a tool_executing event
    /// </summary>
    public static AgentStepEvent ToolExecuting(int stepNumber, string toolName, string? toolInput)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.tool_executing,
            StepNumber = stepNumber,
            ToolName = toolName,
            ToolInput = toolInput?.Length > 500 ? toolInput.Substring(0, 500) + "..." : toolInput
        };
    }

    /// <summary>
    /// Creates a tool_result event
    /// </summary>
    public static AgentStepEvent ToolResult_(int stepNumber, string toolName, string? result, bool success)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.tool_result,
            StepNumber = stepNumber,
            ToolName = toolName,
            ToolResult = result?.Length > 1000 ? result.Substring(0, 1000) + "..." : result,
            ToolSuccess = success
        };
    }

    /// <summary>
    /// Creates a reflecting event
    /// </summary>
    public static AgentStepEvent Reflecting(int stepNumber, string? reflection)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.reflecting,
            StepNumber = stepNumber,
            Thought = reflection
        };
    }

    /// <summary>
    /// Creates a sql_generated event
    /// </summary>
    public static AgentStepEvent SqlGeneratedEvent(int stepNumber, string sql)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.sql_generated,
            StepNumber = stepNumber,
            SqlGenerated = sql
        };
    }

    /// <summary>
    /// Creates a sql_executing event
    /// </summary>
    public static AgentStepEvent SqlExecutingEvent(string sql)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.sql_executing,
            SqlGenerated = sql
        };
    }

    /// <summary>
    /// Creates a completed event with final result
    /// </summary>
    public static AgentStepEvent CompletedEvent(
        int totalSteps,
        string? sql,
        object? result,
        int rowCount,
        string? answer,
        Dictionary<string, object>? metadata = null)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.completed,
            TotalSteps = totalSteps,
            SqlGenerated = sql,
            SqlResult = result,
            RowCount = rowCount,
            Answer = answer,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates an error event
    /// </summary>
    public static AgentStepEvent Error(string message, bool recoverable = false, int? stepNumber = null)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.error,
            ErrorMessage = message,
            Recoverable = recoverable,
            StepNumber = stepNumber
        };
    }

    /// <summary>
    /// Creates a needs_clarification event
    /// </summary>
    public static AgentStepEvent NeedsClarification(
        string sessionId,
        ClarificationType clarificationType,
        string question,
        List<string>? options = null,
        int timeoutSeconds = 300)
    {
        return new AgentStepEvent
        {
            EventType = AgentStepEventType.needs_clarification,
            Metadata = new Dictionary<string, object>
            {
                ["sessionId"] = sessionId,
                ["clarificationType"] = clarificationType.ToString(),
                ["question"] = question,
                ["options"] = options ?? new List<string>(),
                ["timeoutSeconds"] = timeoutSeconds
            }
        };
    }
}
