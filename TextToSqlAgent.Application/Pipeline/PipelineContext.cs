using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Plugins;
using Message = TextToSqlAgent.Infrastructure.Entities.Message;

namespace TextToSqlAgent.Application.Pipeline;

/// <summary>
/// Shared context passed through all pipeline stages.
/// Each stage reads from and writes to this context, enabling data flow between stages.
/// </summary>
public class PipelineContext
{
    // ── Input ──
    /// <summary>Original user question (unmodified).</summary>
    public string UserQuestion { get; set; } = string.Empty;

    /// <summary>Optional conversation ID for multi-turn support.</summary>
    public string? ConversationId { get; set; }

    /// <summary>Conversation history from database.</summary>
    public List<Message>? ConversationHistory { get; set; }

    /// <summary>SSE progress reporter (optional, for streaming endpoints).</summary>
    public IProgress<AgentStageEvent>? Progress { get; set; }

    /// <summary>Callback for streaming SQL tokens (optional).</summary>
    public Action<string>? SqlTokenCallback { get; set; }

    // ── Stage-Produced Data ──
    /// <summary>Question after context enrichment and pronoun resolution.</summary>
    public string EnrichedQuestion { get; set; } = string.Empty;

    /// <summary>Result of query validation (is it database-relevant?).</summary>
    public QueryValidationResult? ValidationResult { get; set; }

    /// <summary>Intent classification result (QUERY/WRITE/DDL/FORBIDDEN).</summary>
    public IntentClassificationResult? IntentClassification { get; set; }

    /// <summary>Deep intent analysis result (target table, query type, etc.).</summary>
    public IntentAnalysis? Intent { get; set; }

    /// <summary>Normalized prompt after cleanup.</summary>
    public NormalizedPrompt? NormalizedPrompt { get; set; }

    /// <summary>Full database schema (cached or freshly scanned).</summary>
    public DatabaseSchema? Schema { get; set; }

    /// <summary>RAG-retrieved relevant schema context (tables, relationships).</summary>
    public RetrievedSchemaContext? SchemaContext { get; set; }

    /// <summary>Available table names from the schema.</summary>
    public List<string> TableNames { get; set; } = new();

    /// <summary>Generated SQL query.</summary>
    public string? GeneratedSql { get; set; }

    /// <summary>SQL generation result with metadata.</summary>
    public SqlGenerationResult? SqlGenerationResult { get; set; }

    /// <summary>SQL execution result (rows, success, error).</summary>
    public SqlExecutionResult? ExecutionResult { get; set; }

    /// <summary>SQL correction history (if self-correction was needed).</summary>
    public List<CorrectionAttempt> Corrections { get; set; } = new();

    /// <summary>Conversation context for multi-turn support.</summary>
    public ConversationContext? ConversationCtx { get; set; }

    /// <summary>Processing steps log.</summary>
    public List<string> Steps { get; set; } = new();

    // ── Output ──
    /// <summary>The final AgentResponse being built progressively by stages.</summary>
    public AgentResponse Response { get; set; } = new();

    // ── Helper Methods ──
    /// <summary>Report progress via SSE if a progress reporter is attached.</summary>
    public void ReportProgress(AgentStage stage, string message, double progress, string? detail = null)
    {
        Progress?.Report(new AgentStageEvent
        {
            Stage = stage,
            Message = message,
            Progress = progress,
            Detail = detail
        });
    }
}
