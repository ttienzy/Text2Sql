using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Two-layer intent classifier:
/// Layer 1: Quick rule-based pattern matching (fast, no LLM)
/// Layer 2: LLM-based classification for ambiguous cases
/// </summary>
public class IntentClassifier : IIntentClassifier
{
    private readonly ILLMClient _llmClient;
    private readonly ILogger<IntentClassifier> _logger;

    // Confidence threshold - below this, use LLM fallback
    // ✅ LOWERED from 0.85 to 0.75 to trust rule-based more
    private const double RuleBasedConfidenceThreshold = 0.75;

    // Minimum confidence to accept classification
    private const double MinimumConfidenceThreshold = 0.65;

    // ═══════════════════════════════════════════════════════════════
    // LAYER 1: RULE-BASED PATTERNS (Quick Block)
    // ═══════════════════════════════════════════════════════════════

    // FORBIDDEN - Highest priority, most dangerous
    private static readonly string[] ForbiddenPatterns =
    {
        "drop table", "drop database", "truncate table", "truncate ",
        "delete from", "delete ", "delete all", "remove all",
        "xóa bảng", "xóa toàn bộ", "xóa hết", "xoá", "xóa dữ liệu",
        "purge ", "wipe ", "clear all data", "clear table",
        "destroy", "remove data"
    };

    // WRITE - INSERT patterns
    private static readonly string[] InsertPatterns =
    {
        "insert into", "thêm", "add new", "tạo mới", "create new",
        "đăng ký", "nhập dữ liệu", "thêm dữ liệu", "insert record"
    };

    // WRITE - UPDATE patterns
    private static readonly string[] UpdatePatterns =
    {
        "update ", "cập nhật", "sửa", "modify", "change",
        "set ", "đổi", "chỉnh sửa", "edit"
    };

    // DDL - INDEX patterns
    private static readonly string[] IndexPatterns =
    {
        "create index", "tạo index", "add index", "thêm index",
        "drop index", "optimize", "tối ưu", "index on"
    };

    // DDL - PROCEDURE/FUNCTION patterns
    private static readonly string[] ProcedurePatterns =
    {
        "create procedure", "create function", "tạo procedure",
        "tạo function", "stored procedure", "create proc",
        "alter procedure", "alter function"
    };

    // DDL - ALTER TABLE patterns
    private static readonly string[] AlterPatterns =
    {
        "alter table", "add column", "thêm cột", "modify column",
        "đổi kiểu", "rename column", "drop column"
    };

    // DDL - VIEW patterns
    private static readonly string[] ViewPatterns =
    {
        "create view", "tạo view", "alter view", "virtual table"
    };

    // QUERY - Common read patterns (lowest priority, default)
    private static readonly string[] QueryPatterns =
    {
        "select ", "liệt kê", "tìm", "xem", "show", "get",
        "thống kê", "báo cáo", "report", "count", "sum",
        "bao nhiêu", "có bao nhiêu", "danh sách", "list"
    };

    public IntentClassifier(
        ILLMClient llmClient,
        ILogger<IntentClassifier> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<IntentClassificationResult> ClassifyAsync(
        string question,
        string? conversationHistory = null,
        string? databaseContext = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return CreateUnknownResult(question, "Empty question");
        }

        _logger.LogDebug("[IntentClassifier] Classifying: {Question}", question);

        // Layer 1: Quick rule-based check
        var quickResult = QuickBlockCheck(question);
        if (quickResult != null)
        {
            _logger.LogInformation(
                "[IntentClassifier] Quick-block matched: {Intent} (confidence: {Confidence})",
                quickResult.Intent, quickResult.Confidence);
            return quickResult;
        }

        // Layer 2: Rule-based classification with pattern matching
        var ruleResult = ClassifyByRules(question);

        _logger.LogDebug(
            "[IntentClassifier] Rule-based result: {Intent} (confidence: {Confidence})",
            ruleResult.Intent, ruleResult.Confidence);

        // If high confidence, return immediately
        if (ruleResult.Confidence >= RuleBasedConfidenceThreshold)
        {
            _logger.LogInformation(
                "[IntentClassifier] High confidence rule-based classification: {Intent}",
                ruleResult.Intent);
            return ruleResult;
        }

        // Layer 3: LLM fallback for ambiguous cases
        try
        {
            var llmResult = await ClassifyWithLlmAsync(
                question, conversationHistory, databaseContext, ct);

            _logger.LogInformation(
                "[IntentClassifier] LLM classification: {Intent} (confidence: {Confidence})",
                llmResult.Intent, llmResult.Confidence);

            return llmResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IntentClassifier] LLM classification failed, using rule-based result");
            return ruleResult;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // LAYER 1: QUICK BLOCK - Immediate rejection for dangerous patterns
    // ═══════════════════════════════════════════════════════════════

    private IntentClassificationResult? QuickBlockCheck(string question)
    {
        var lower = question.ToLowerInvariant();

        foreach (var pattern in ForbiddenPatterns)
        {
            if (lower.Contains(pattern))
            {
                _logger.LogWarning(
                    "[IntentClassifier] FORBIDDEN pattern detected: {Pattern}",
                    pattern);

                return new IntentClassificationResult
                {
                    Intent = IntentCategory.Forbidden,
                    Route = PipelineRoute.Forbidden,
                    Confidence = 0.99,
                    Reasoning = $"Quick-block detected dangerous pattern: '{pattern}'",
                    NormalizedQuery = question,
                    Method = ClassificationMethod.RuleBased,
                    MatchedKeywords = new List<string> { pattern },
                    ForbiddenReason = $"Detected data deletion operation: {pattern}",
                    SafeAlternatives = GetSafeAlternatives()
                };
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // LAYER 2: RULE-BASED CLASSIFICATION
    // ═══════════════════════════════════════════════════════════════

    private IntentClassificationResult ClassifyByRules(string question)
    {
        var lower = question.ToLowerInvariant();
        var matchedKeywords = new List<string>();
        var scores = new Dictionary<IntentCategory, double>();

        // Check each pattern category
        CheckPatterns(lower, InsertPatterns, IntentCategory.Insert, scores, matchedKeywords);
        CheckPatterns(lower, UpdatePatterns, IntentCategory.Update, scores, matchedKeywords);
        CheckPatterns(lower, IndexPatterns, IntentCategory.DdlIndex, scores, matchedKeywords);
        CheckPatterns(lower, ProcedurePatterns, IntentCategory.DdlProcedure, scores, matchedKeywords);
        CheckPatterns(lower, AlterPatterns, IntentCategory.DdlAlter, scores, matchedKeywords);
        CheckPatterns(lower, ViewPatterns, IntentCategory.DdlView, scores, matchedKeywords);
        CheckPatterns(lower, QueryPatterns, IntentCategory.Query, scores, matchedKeywords);

        // Find highest scoring intent
        if (scores.Count == 0)
        {
            // No patterns matched - DEFAULT TO QUERY with HIGH confidence
            // This is the safe default for database assistant
            return new IntentClassificationResult
            {
                Intent = IntentCategory.Query,
                Route = PipelineRoute.Query,
                Confidence = 0.90, // ✅ HIGH confidence to skip LLM fallback
                Reasoning = "No specific patterns matched, defaulting to Query (safe default)",
                NormalizedQuery = question,
                Method = ClassificationMethod.RuleBased
            };
        }

        var topIntent = scores.OrderByDescending(x => x.Value).First();
        var confidence = topIntent.Value;

        return new IntentClassificationResult
        {
            Intent = topIntent.Key,
            Route = ResolveRoute(topIntent.Key),
            Confidence = confidence,
            Reasoning = $"Matched {matchedKeywords.Count} pattern(s): {string.Join(", ", matchedKeywords.Take(3))}",
            NormalizedQuery = question,
            Method = ClassificationMethod.RuleBased,
            MatchedKeywords = matchedKeywords
        };
    }

    private void CheckPatterns(
        string lowerQuestion,
        string[] patterns,
        IntentCategory intent,
        Dictionary<IntentCategory, double> scores,
        List<string> matchedKeywords)
    {
        var matchCount = 0;
        foreach (var pattern in patterns)
        {
            if (lowerQuestion.Contains(pattern))
            {
                matchCount++;
                matchedKeywords.Add(pattern);
                _logger.LogDebug("[IntentClassifier] Pattern matched: '{Pattern}' → {Intent}", pattern, intent);
            }
        }

        if (matchCount > 0)
        {
            // Confidence increases with more matches
            var confidence = Math.Min(0.7 + (matchCount * 0.1), 0.95);
            scores[intent] = confidence;
            _logger.LogDebug("[IntentClassifier] {Intent} score: {Confidence} ({Count} matches)", intent, confidence, matchCount);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // LAYER 3: LLM-BASED CLASSIFICATION
    // ═══════════════════════════════════════════════════════════════

    private async Task<IntentClassificationResult> ClassifyWithLlmAsync(
        string question,
        string? conversationHistory,
        string? databaseContext,
        CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(databaseContext);
        var userContent = BuildUserContent(question, conversationHistory);

        var response = await _llmClient.CompleteWithSystemPromptAsync(
            systemPrompt,
            userContent,
            ct);

        // Parse LLM response
        var llmResponse = ParseLlmResponse(response);

        return MapLlmResponseToResult(llmResponse, question);
    }

    private string BuildSystemPrompt(string? databaseContext)
    {
        var dbCtx = string.IsNullOrWhiteSpace(databaseContext)
            ? ""
            : $"\n\n## Database Context\n{databaseContext}";

        return $@"You are an Intent Classifier for a Database Assistant system.
Your ONLY task is to classify user intent and return JSON.
DO NOT explain, DO NOT answer questions, DO NOT generate SQL.
{dbCtx}

## Valid Intent Types

| Intent           | Description                                                    |
|------------------|----------------------------------------------------------------|
| QUERY            | Read data: SELECT, search, statistics, reports, view lists    |
| INSERT           | Add new data to table                                          |
| UPDATE           | Update existing data (not delete)                              |
| DDL_INDEX        | Create, modify, or optimize indexes                            |
| DDL_PROCEDURE    | Create or modify stored procedures, functions                  |
| DDL_ALTER        | Add/modify/remove columns, change data types, rename           |
| DDL_VIEW         | Create or modify views                                         |
| FORBIDDEN        | DELETE data: DELETE, DROP TABLE, TRUNCATE, PURGE               |
| OFF_TOPIC        | Not related to database (weather, etc.)                        |
| UNKNOWN          | Cannot determine clearly                                       |

## FORBIDDEN - Absolute Rules (NO EXCEPTIONS)

Any request with intent to permanently delete data → classify as FORBIDDEN:
- Direct SQL: DELETE, DROP, TRUNCATE, PURGE
- Natural language: ""delete records"", ""remove users"", ""clear table"", ""delete all""
- Even if user says ""just testing"", ""demo only"" → still FORBIDDEN

## Output Format - PURE JSON ONLY

Return EXACTLY this format:
{{
  ""intent"": ""QUERY|INSERT|UPDATE|DDL_INDEX|DDL_PROCEDURE|DDL_ALTER|DDL_VIEW|FORBIDDEN|OFF_TOPIC|UNKNOWN"",
  ""confidence"": 0.0,
  ""reason"": ""Brief explanation (max 1 sentence)"",
  ""normalized_query"": ""Standardized version resolving pronouns"",
  ""entities"": [""table or column names mentioned""],
  ""warnings"": [""warnings if any""],
  ""forbidden_reason"": null,
  ""safe_alternatives"": []
}}

For FORBIDDEN, also fill:
""forbidden_reason"": ""Specific reason why blocked"",
""safe_alternatives"": [""soft delete"", ""archive table"", ""inactive flag""]";
    }

    private string BuildUserContent(string question, string? conversationHistory)
    {
        if (string.IsNullOrWhiteSpace(conversationHistory))
        {
            return $"User message: {question}";
        }

        return $@"## Conversation history (for pronoun resolution)
{conversationHistory}

## Message to classify
{question}";
    }

    private LlmClassificationResponse ParseLlmResponse(string response)
    {
        // Strip markdown fences if present
        var cleaned = response.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned[3..];

        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3];

        cleaned = cleaned.Trim();

        try
        {
            return JsonSerializer.Deserialize<LlmClassificationResponse>(cleaned)
                ?? throw new InvalidOperationException("Failed to parse LLM response");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[IntentClassifier] Failed to parse LLM JSON: {Response}", cleaned);
            throw;
        }
    }

    private IntentClassificationResult MapLlmResponseToResult(
        LlmClassificationResponse llmResponse,
        string originalQuestion)
    {
        var intent = ParseIntentType(llmResponse.Intent);
        var route = ResolveRoute(intent);
        var confidence = llmResponse.Confidence;

        // If confidence too low, mark as Unknown
        if (confidence < MinimumConfidenceThreshold && intent != IntentCategory.Forbidden)
        {
            intent = IntentCategory.Unknown;
            route = PipelineRoute.Reject;
        }

        // Forbidden always routes correctly regardless of confidence
        if (intent == IntentCategory.Forbidden)
        {
            route = PipelineRoute.Forbidden;
        }

        return new IntentClassificationResult
        {
            Intent = intent,
            Route = route,
            Confidence = confidence,
            Reasoning = llmResponse.Reason,
            NormalizedQuery = string.IsNullOrWhiteSpace(llmResponse.NormalizedQuery)
                ? originalQuestion
                : llmResponse.NormalizedQuery,
            Method = ClassificationMethod.LlmBased,
            DetectedEntities = llmResponse.Entities,
            Warnings = llmResponse.Warnings,
            ForbiddenReason = llmResponse.ForbiddenReason,
            SafeAlternatives = intent == IntentCategory.Forbidden
                ? GetSafeAlternatives()
                : new List<SafeAlternative>()
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private IntentCategory ParseIntentType(string raw) => raw.ToUpperInvariant() switch
    {
        "QUERY" => IntentCategory.Query,
        "INSERT" => IntentCategory.Insert,
        "UPDATE" => IntentCategory.Update,
        "DDL_INDEX" => IntentCategory.DdlIndex,
        "DDL_PROCEDURE" => IntentCategory.DdlProcedure,
        "DDL_ALTER" => IntentCategory.DdlAlter,
        "DDL_VIEW" => IntentCategory.DdlView,
        "FORBIDDEN" => IntentCategory.Forbidden,
        "OFF_TOPIC" => IntentCategory.OffTopic,
        _ => IntentCategory.Unknown
    };

    private PipelineRoute ResolveRoute(IntentCategory intent) => intent switch
    {
        IntentCategory.Query => PipelineRoute.Query,
        IntentCategory.Insert or IntentCategory.Update => PipelineRoute.Write,
        IntentCategory.DdlIndex or IntentCategory.DdlProcedure
            or IntentCategory.DdlAlter or IntentCategory.DdlView => PipelineRoute.Ddl,
        IntentCategory.Forbidden => PipelineRoute.Forbidden,
        IntentCategory.OffTopic or IntentCategory.Unknown => PipelineRoute.Reject,
        _ => PipelineRoute.Reject
    };

    private List<SafeAlternative> GetSafeAlternatives() => new()
    {
        new SafeAlternative
        {
            Type = SafeAlternativeType.SoftDelete,
            Title = "Soft Delete",
            Description = "Add is_deleted column instead of physical deletion",
            ExampleSql = "ALTER TABLE users ADD COLUMN is_deleted BOOLEAN DEFAULT FALSE;\nUPDATE users SET is_deleted = TRUE WHERE id = 42;"
        },
        new SafeAlternative
        {
            Type = SafeAlternativeType.Archive,
            Title = "Archive Table",
            Description = "Move records to _archive table before deactivation",
            ExampleSql = "INSERT INTO users_archive SELECT * FROM users WHERE id = 42;\nUPDATE users SET status = 'archived' WHERE id = 42;"
        },
        new SafeAlternative
        {
            Type = SafeAlternativeType.InactiveFlag,
            Title = "Inactive Flag",
            Description = "Add status = 'inactive' column instead of deletion",
            ExampleSql = "UPDATE users SET status = 'inactive', deactivated_at = NOW() WHERE id = 42;"
        },
        new SafeAlternative
        {
            Type = SafeAlternativeType.AuditLog,
            Title = "Audit Log",
            Description = "Log reason and timestamp to audit_log table",
            ExampleSql = "INSERT INTO audit_log (table_name, record_id, action, reason) VALUES ('users', 42, 'deactivate', 'User requested account closure');"
        }
    };

    private IntentClassificationResult CreateUnknownResult(string question, string reason)
    {
        return new IntentClassificationResult
        {
            Intent = IntentCategory.Unknown,
            Route = PipelineRoute.Reject,
            Confidence = 0.0,
            Reasoning = reason,
            NormalizedQuery = question,
            Method = ClassificationMethod.RuleBased
        };
    }
}

/// <summary>
/// LLM response model for intent classification
/// </summary>
internal class LlmClassificationResponse
{
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty; // Alias for Reasoning
    public string NormalizedQuery { get; set; } = string.Empty;
    public List<string> Entities { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? ForbiddenReason { get; set; }
}
