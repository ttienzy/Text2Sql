using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Intent categories for routing to appropriate pipelines
/// </summary>
public enum IntentCategory
{
    /// <summary>SELECT queries, statistics, reports</summary>
    Query,

    /// <summary>INSERT operations - adding new data</summary>
    Insert,

    /// <summary>UPDATE operations - modifying existing data</summary>
    Update,

    /// <summary>DELETE operations with WHERE clause - removing specific data (requires confirmation)</summary>
    Delete,

    /// <summary>UPSERT/MERGE operations - insert or update pattern</summary>
    Upsert,

    /// <summary>CREATE/DROP INDEX operations</summary>
    DdlIndex,

    /// <summary>CREATE/ALTER PROCEDURE/FUNCTION operations</summary>
    DdlProcedure,

    /// <summary>ALTER TABLE operations (ADD/MODIFY columns)</summary>
    DdlAlter,

    /// <summary>CREATE/ALTER VIEW operations</summary>
    DdlView,

    /// <summary>DROP TABLE/TRUNCATE/DELETE ALL - forbidden operations (no WHERE, mass destruction)</summary>
    Forbidden,

    /// <summary>Off-topic questions not related to database</summary>
    OffTopic,

    /// <summary>Cannot classify with confidence</summary>
    Unknown
}

/// <summary>
/// Pipeline routing decision based on intent
/// </summary>
public enum PipelineRoute
{
    /// <summary>Route to existing QUERY pipeline (Simple/Medium/Complex)</summary>
    Query,

    /// <summary>Route to DML pipeline (INSERT/UPDATE/DELETE/UPSERT) — requires confirmation</summary>
    Dml,

    /// <summary>Route to new WRITE pipeline (INSERT/UPDATE) — legacy alias for Dml</summary>
    Write = Dml,

    /// <summary>Route to new DDL pipeline (INDEX/PROC/ALTER/VIEW)</summary>
    Ddl,

    /// <summary>Route to FORBIDDEN pipeline (hard rejection)</summary>
    Forbidden,

    /// <summary>Reject immediately (off-topic or unknown)</summary>
    Reject
}

/// <summary>
/// Classification method used
/// </summary>
public enum ClassificationMethod
{
    /// <summary>Fast rule-based pattern matching</summary>
    RuleBased,

    /// <summary>LLM-based classification for ambiguous cases</summary>
    LlmBased,

    /// <summary>Hybrid: rule-based + LLM validation</summary>
    Hybrid
}

/// <summary>
/// Result of intent classification
/// </summary>
public class IntentClassificationResult
{
    /// <summary>Detected intent category</summary>
    public IntentCategory Intent { get; set; }

    /// <summary>Sub-intent for finer routing (e.g. "single", "bulk", "targeted", "mass")</summary>
    public string? SubIntent { get; set; }

    /// <summary>Recommended pipeline route</summary>
    public PipelineRoute Route { get; set; }

    /// <summary>Risk level of the operation</summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

    /// <summary>Confidence score (0.0 - 1.0)</summary>
    public double Confidence { get; set; }

    /// <summary>Complexity score of the query (0.0 - 1.0) to determine routing to AgentLoop</summary>
    public double ComplexityScore { get; set; } = 0.0;

    /// <summary>Reasoning for complexity classification</summary>
    public string ComplexityReasoning { get; set; } = string.Empty;

    /// <summary>Reasoning for classification decision</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>Normalized query (resolved pronouns, standardized)</summary>
    public string NormalizedQuery { get; set; } = string.Empty;

    /// <summary>Classification method used</summary>
    public ClassificationMethod Method { get; set; }

    /// <summary>Detected entities (table names, column names)</summary>
    public List<string> DetectedEntities { get; set; } = new();

    /// <summary>Warnings about potential issues</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Keywords that triggered classification</summary>
    public List<string> MatchedKeywords { get; set; } = new();

    /// <summary>Reason for forbidden classification (null if not forbidden)</summary>
    public string? ForbiddenReason { get; set; }

    /// <summary>Safe alternatives for forbidden operations</summary>
    public List<SafeAlternative> SafeAlternatives { get; set; } = new();
}

/// <summary>
/// Safe alternative suggestion for forbidden operations
/// </summary>
public class SafeAlternative
{
    public SafeAlternativeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ExampleSql { get; set; }
}

public enum SafeAlternativeType
{
    SoftDelete,      // is_deleted flag
    Archive,         // Move to archive table
    InactiveFlag,    // status = 'inactive'
    AuditLog         // Log before deactivate
}

/// <summary>
/// Risk level for DML/DDL operations — determines UI treatment and confirmation requirements
/// </summary>
public enum RiskLevel
{
    /// <summary>Read-only or metadata operations (QUERY, SCHEMA_DESCRIBE)</summary>
    Low,

    /// <summary>Single-row INSERT, CREATE INDEX/VIEW (reversible)</summary>
    Medium,

    /// <summary>Multi-row UPDATE, ALTER TABLE, mass INSERT</summary>
    High,

    /// <summary>DELETE with WHERE, DROP operations — requires explicit confirmation</summary>
    Critical
}

/// <summary>
/// LLM response format for intent classification
/// </summary>
internal class LlmClassificationResponse
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("complexityScore")]
    public double ComplexityScore { get; set; } = 0.0;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("normalized_query")]
    public string NormalizedQuery { get; set; } = string.Empty;

    [JsonPropertyName("entities")]
    public List<string> Entities { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("forbidden_reason")]
    public string? ForbiddenReason { get; set; }

    [JsonPropertyName("safe_alternatives")]
    public List<string> SafeAlternatives { get; set; } = new();
}

/// <summary>
/// Extension methods for IntentClassificationResult
/// </summary>
public static class IntentClassificationExtensions
{
    /// <summary>
    /// Convert IntentClassificationResult to IntentSummary (filtered for client)
    /// Excludes internal reasoning and normalized query for security
    /// </summary>
    public static IntentSummary ToIntentSummary(this IntentClassificationResult result)
    {
        return new IntentSummary
        {
            Type = result.Intent,
            Route = result.Route,
            Confidence = result.Confidence,
            DetectedEntities = result.DetectedEntities,
            MatchedKeywords = result.MatchedKeywords
        };
    }

    /// <summary>
    /// Create default intent summary for cases where classification is not available
    /// </summary>
    public static IntentSummary CreateDefaultQueryIntent()
    {
        return new IntentSummary
        {
            Type = IntentCategory.Query,
            Route = PipelineRoute.Query,
            Confidence = 1.0,
            DetectedEntities = new List<string>(),
            MatchedKeywords = new List<string>()
        };
    }
}
