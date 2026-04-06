using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Application.Services;

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
    private readonly IIntentCacheService? _cacheService;

    // Confidence threshold - below this, use LLM fallback
    // ✅ REFACTORED: Keep at 0.75 - with new weighted scoring, this is more accurate
    private const double RuleBasedConfidenceThreshold = 0.75;

    // Minimum confidence to accept classification
    // ✅ FIX 2: Lowered from 0.50 to 0.35 to accept more classifications
    // before routing to REJECT
    private const double MinimumConfidenceThreshold = 0.35;

    // ═══════════════════════════════════════════════════════════════
    // LAYER 1: RULE-BASED PATTERNS (Quick Block)
    // REFACTORED: Use word boundaries to avoid false positives
    // ═══════════════════════════════════════════════════════════════

    // FORBIDDEN - Highest priority, most dangerous
    // REFACTORED: Use Regex patterns with word boundaries
    private static readonly (string Pattern, Regex Regex, double Weight)[] ForbiddenPatterns = new[]
    {
        // SQL dangerous operations - exact matches with boundary
        (@"\bdrop\s+table\b", new Regex(@"\bdrop\s+table\b", RegexOptions.IgnoreCase), 1.0),
        (@"\bdrop\s+database\b", new Regex(@"\bdrop\s+database\b", RegexOptions.IgnoreCase), 1.0),
        (@"\btruncate\s+table\b", new Regex(@"\btruncate\s+table\b", RegexOptions.IgnoreCase), 1.0),
        (@"\btruncate\b", new Regex(@"\btruncate\b", RegexOptions.IgnoreCase), 0.95),
        (@"\bdelete\s+from\b", new Regex(@"\bdelete\s+from\b", RegexOptions.IgnoreCase), 1.0),
        // Vietnamese - dangerous
        (@"\bxóa\s+bảng\b", new Regex(@"\bxóa\s+bảng\b", RegexOptions.IgnoreCase), 1.0),
        (@"\bxóa\s+toàn\s+bộ\b", new Regex(@"\bxóa\s+toàn\s+bộ\b", RegexOptions.IgnoreCase), 1.0),
        (@"\bxóa\s+hết\b", new Regex(@"\bxóa\s+hết\b", RegexOptions.IgnoreCase), 1.0),
        (@"\bxóa\s+dữ\s+liệu\b", new Regex(@"\bxóa\s+dữ\s+liệu\b", RegexOptions.IgnoreCase), 1.0),
        (@"\bxoá\b", new Regex(@"\bxoá\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bdestroy\b", new Regex(@"\bdestroy\b", RegexOptions.IgnoreCase), 0.9),
    };

    // WRITE - INSERT patterns with EXPANDED coverage
    private static readonly (string Pattern, Regex Regex, double Weight)[] InsertPatterns = new[]
    {
        // English
        (@"\binsert\s+into\b", new Regex(@"\binsert\s+into\b", RegexOptions.IgnoreCase), 0.95),
        (@"\binsert\s+", new Regex(@"\binsert\s+", RegexOptions.IgnoreCase), 0.80), // ✅ NEW: catch "insert" alone
        (@"\badd\s+new\b", new Regex(@"\badd\s+new\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bcreate\s+new\b", new Regex(@"\bcreate\s+new\b", RegexOptions.IgnoreCase), 0.85),
        (@"\binsert\s+record\b", new Regex(@"\binsert\s+record\b", RegexOptions.IgnoreCase), 0.9),
        // ✅ FIX 4: Add more INSERT patterns for common phrases
        (@"\badd\s+a\s+new\b", new Regex(@"\badd\s+a\s+new\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bcreate\s+user\b", new Regex(@"\bcreate\s+user\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bregister\b", new Regex(@"\bregister\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bsign\s+up\b", new Regex(@"\bsign\s+up\b", RegexOptions.IgnoreCase), 0.85),
        (@"\badd\s+a\b", new Regex(@"\badd\s+a\b", RegexOptions.IgnoreCase), 0.80),
        (@"\bnew\s+record\b", new Regex(@"\bnew\s+record\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bsave\b", new Regex(@"\bsave\b", RegexOptions.IgnoreCase), 0.75),
        // Vietnamese - EXPANDED with more specific patterns
        (@"\bthêm\s+(?!cột\b)", new Regex(@"\bthêm\s+(?!cột\b)", RegexOptions.IgnoreCase), 0.85), // "thêm" but NOT "thêm cột"
        (@"\btạo\s+mới\b", new Regex(@"\btạo\s+mới\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bđăng\s+ký\b", new Regex(@"\bđăng\s+ký\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bnhập\s+dữ\s+liệu\b", new Regex(@"\bnhập\s+dữ\s+liệu\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bthêm\s+dữ\s+liệu\b", new Regex(@"\bthêm\s+dữ\s+liệu\b", RegexOptions.IgnoreCase), 0.9),
        (@"\btạo\s+bản\s+ghi\b", new Regex(@"\btạo\s+bản\s+ghi\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bthêm\s+hàng\b", new Regex(@"\bthêm\s+hàng\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bchèn\b", new Regex(@"\bchèn\b", RegexOptions.IgnoreCase), 0.85),
    };

    // WRITE - UPDATE patterns - IMPROVED with more patterns
    private static readonly (string Pattern, Regex Regex, double Weight)[] UpdatePatterns = new[]
    {
        // English - require word boundary
        (@"\bupdate\s+", new Regex(@"\bupdate\s+", RegexOptions.IgnoreCase), 0.95),
        (@"\bmodify\s+", new Regex(@"\bmodify\s+", RegexOptions.IgnoreCase), 0.85),
        (@"\bchange\s+", new Regex(@"\bchange\s+", RegexOptions.IgnoreCase), 0.8),
        (@"\bset\s+\w+\s*=", new Regex(@"\bset\s+\w+\s*=", RegexOptions.IgnoreCase), 0.80), // "set status = active"
        (@"\bedit\b", new Regex(@"\bedit\b", RegexOptions.IgnoreCase), 0.85),
        // ✅ FIX 4: Add more UPDATE patterns
        (@"\bupdate\s+user\b", new Regex(@"\bupdate\s+user\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bupdate\s+the\b", new Regex(@"\bupdate\s+the\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bupdate\s+information\b", new Regex(@"\bupdate\s+information\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bupdate\s+details\b", new Regex(@"\bupdate\s+details\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bmodify\s+user\b", new Regex(@"\bmodify\s+user\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bchange\s+password\b", new Regex(@"\bchange\s+password\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bchange\s+status\b", new Regex(@"\bchange\s+status\b", RegexOptions.IgnoreCase), 0.90),
        (@"\bsave\s+(?:to|into)\b", new Regex(@"\bsave\s+(?:to|into)\b", RegexOptions.IgnoreCase), 0.78), // "save to database"
        // Vietnamese - EXPANDED
        (@"\bcập\s+nhật\b", new Regex(@"\bcập\s+nhật\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bsửa\b", new Regex(@"\bsửa\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bđổi\b", new Regex(@"\bđổi\b", RegexOptions.IgnoreCase), 0.8),
        (@"\bchỉnh\s+sửa\b", new Regex(@"\bchỉnh\s+sửa\b", RegexOptions.IgnoreCase), 0.85),
    };

    // DDL - INDEX patterns
    private static readonly (string Pattern, Regex Regex, double Weight)[] IndexPatterns = new[]
    {
        (@"\bcreate\s+index\b", new Regex(@"\bcreate\s+index\b", RegexOptions.IgnoreCase), 0.95),
        (@"\badd\s+index\b", new Regex(@"\badd\s+index\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bdrop\s+index\b", new Regex(@"\bdrop\s+index\b", RegexOptions.IgnoreCase), 0.9),
        (@"\boptimize\s+table\b", new Regex(@"\boptimize\s+table\b", RegexOptions.IgnoreCase), 0.85),
        // Vietnamese
        (@"\btạo\s+index\b", new Regex(@"\btạo\s+index\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bthêm\s+index\b", new Regex(@"\bthêm\s+index\b", RegexOptions.IgnoreCase), 0.9),
        (@"\btối\s+ưu\s+bảng\b", new Regex(@"\btối\s+ưu\s+bảng\b", RegexOptions.IgnoreCase), 0.85),
    };

    // DDL - PROCEDURE/FUNCTION patterns
    private static readonly (string Pattern, Regex Regex, double Weight)[] ProcedurePatterns = new[]
    {
        (@"\bcreate\s+procedure\b", new Regex(@"\bcreate\s+procedure\b", RegexOptions.IgnoreCase), 0.95),
        (@"\bcreate\s+function\b", new Regex(@"\bcreate\s+function\b", RegexOptions.IgnoreCase), 0.95),
        (@"\balter\s+procedure\b", new Regex(@"\balter\s+procedure\b", RegexOptions.IgnoreCase), 0.9),
        (@"\balter\s+function\b", new Regex(@"\balter\s+function\b", RegexOptions.IgnoreCase), 0.9),
        // Vietnamese
        (@"\btạo\s+procedure\b", new Regex(@"\btạo\s+procedure\b", RegexOptions.IgnoreCase), 0.9),
        (@"\btạo\s+function\b", new Regex(@"\btạo\s+function\b", RegexOptions.IgnoreCase), 0.9),
    };

    // DDL - ALTER TABLE patterns - EXPANDED Vietnamese
    private static readonly (string Pattern, Regex Regex, double Weight)[] AlterPatterns = new[]
    {
        (@"\balter\s+table\b", new Regex(@"\balter\s+table\b", RegexOptions.IgnoreCase), 0.95),
        (@"\badd\s+column\b", new Regex(@"\badd\s+column\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bdrop\s+column\b", new Regex(@"\bdrop\s+column\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bmodify\s+column\b", new Regex(@"\bmodify\s+column\b", RegexOptions.IgnoreCase), 0.85),
        (@"\brename\s+column\b", new Regex(@"\brename\s+column\b", RegexOptions.IgnoreCase), 0.85),
        // Vietnamese - EXPANDED
        (@"\bthêm\s+cột\b", new Regex(@"\bthêm\s+cột\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bxoá\s+cột\b", new Regex(@"\bxoá\s+cột\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bxóa\s+cột\b", new Regex(@"\bxóa\s+cột\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bđổi\s+kiểu\b", new Regex(@"\bđổi\s+kiểu\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bđổi\s+tên\s+cột\b", new Regex(@"\bđổi\s+tên\s+cột\b", RegexOptions.IgnoreCase), 0.85),
        (@"\btạo\s+bảng\b", new Regex(@"\btạo\s+bảng\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bxoá\s+bảng\b", new Regex(@"\bxoá\s+bảng\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bxóa\s+bảng\b", new Regex(@"\bxóa\s+bảng\b", RegexOptions.IgnoreCase), 0.9),
        (@"\btạo\s+cột\b", new Regex(@"\btạo\s+cột\b", RegexOptions.IgnoreCase), 0.9),
    };

    // DDL - VIEW patterns
    private static readonly (string Pattern, Regex Regex, double Weight)[] ViewPatterns = new[]
    {
        (@"\bcreate\s+view\b", new Regex(@"\bcreate\s+view\b", RegexOptions.IgnoreCase), 0.95),
        (@"\balter\s+view\b", new Regex(@"\balter\s+view\b", RegexOptions.IgnoreCase), 0.9),
        // Vietnamese
        (@"\btạo\s+view\b", new Regex(@"\btạo\s+view\b", RegexOptions.IgnoreCase), 0.9),
    };

    // QUERY - Common read patterns - EXPANDED Vietnamese
    // LOWER default confidence to trigger LLM more often for ambiguous cases
    private static readonly (string Pattern, Regex Regex, double Weight)[] QueryPatterns = new[]
    {
        // English - High confidence patterns
        (@"\bselect\s+", new Regex(@"\bselect\s+", RegexOptions.IgnoreCase), 0.95),
        (@"\bshow\s+", new Regex(@"\bshow\s+", RegexOptions.IgnoreCase), 0.85),
        (@"\blist\b", new Regex(@"\blist\b", RegexOptions.IgnoreCase), 0.8),
        (@"\bdisplay\b", new Regex(@"\bdisplay\b", RegexOptions.IgnoreCase), 0.8),
        (@"\bview\b", new Regex(@"\bview\b", RegexOptions.IgnoreCase), 0.8),
        // Statistics
        (@"\bcount\s+", new Regex(@"\bcount\s+", RegexOptions.IgnoreCase), 0.9),
        (@"\bsum\s+", new Regex(@"\bsum\s+", RegexOptions.IgnoreCase), 0.9),
        (@"\baverage\s+", new Regex(@"\baverage\s+", RegexOptions.IgnoreCase), 0.9),
        (@"\bmin\s+", new Regex(@"\bmin\s+", RegexOptions.IgnoreCase), 0.9),
        (@"\bmax\s+", new Regex(@"\bmax\s+", RegexOptions.IgnoreCase), 0.9),
        // Vietnamese - EXPANDED
        (@"\bliệt\s+kê\b", new Regex(@"\bliệt\s+kê\b", RegexOptions.IgnoreCase), 0.85),
        (@"\btìm\b", new Regex(@"\btìm\b", RegexOptions.IgnoreCase), 0.8),
        (@"\bxem\b", new Regex(@"\bxem\b", RegexOptions.IgnoreCase), 0.8),
        (@"\blấy\b", new Regex(@"\blấy\b", RegexOptions.IgnoreCase), 0.8),
        (@"\bhiển\s+thị\b", new Regex(@"\bhiển\s+thị\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bthống\s+kê\b", new Regex(@"\bthống\s+kê\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bbáo\s+cáo\b", new Regex(@"\bbáo\s+cáo\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bcó\s+bao\s+nhiêu\b", new Regex(@"\bcó\s+bao\s+nhiêu\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bdanh\s+sách\b", new Regex(@"\bdanh\s+sách\b", RegexOptions.IgnoreCase), 0.85),
        (@"\bđếm\b", new Regex(@"\bđếm\b", RegexOptions.IgnoreCase), 0.9),
        (@"\bkiểm\s+tra\b", new Regex(@"\bkiểm\s+tra\b", RegexOptions.IgnoreCase), 0.8),
        (@"\bxem\s+xét\b", new Regex(@"\bxem\s+xét\b", RegexOptions.IgnoreCase), 0.8),
    };

    public IntentClassifier(
        ILLMClient llmClient,
        ILogger<IntentClassifier> logger,
        IIntentCacheService? cacheService = null)
    {
        _llmClient = llmClient;
        _logger = logger;
        _cacheService = cacheService;

        if (_cacheService != null)
        {
            _logger.LogInformation("[IntentClassifier] Intent caching ENABLED");
        }
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

        // ✅ NEW: Check cache first
        if (_cacheService != null)
        {
            var cachedResult = await _cacheService.GetCachedAsync(question, ct);
            if (cachedResult != null)
            {
                _logger.LogInformation(
                    "[IntentClassifier] ✅ CACHE HIT: {Intent} (confidence: {Confidence})",
                    cachedResult.Intent, cachedResult.Confidence);
                return cachedResult;
            }
        }

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

        // If high confidence, cache and return immediately
        if (ruleResult.Confidence >= RuleBasedConfidenceThreshold)
        {
            _logger.LogInformation(
                "[IntentClassifier] High confidence rule-based classification: {Intent}",
                ruleResult.Intent);

            // ✅ Cache the high-confidence result
            if (_cacheService != null)
            {
                await _cacheService.CacheAsync(question, ruleResult, ct);
            }

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

            // ✅ Cache LLM result if high confidence
            if (_cacheService != null && llmResult.Confidence >= 0.75)
            {
                await _cacheService.CacheAsync(question, llmResult, ct);
                _logger.LogInformation("[IntentClassifier] Cached LLM result");
            }

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

        foreach (var (pattern, regex, weight) in ForbiddenPatterns)
        {
            if (regex.IsMatch(lower))
            {
                _logger.LogWarning(
                    "[IntentClassifier] FORBIDDEN pattern detected: {Pattern} (weight: {Weight})",
                    pattern, weight);

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
    // REFACTORED: Use weighted scoring instead of count-based
    // ═══════════════════════════════════════════════════════════════

    private IntentClassificationResult ClassifyByRules(string question)
    {
        var lower = question.ToLowerInvariant();
        var matchedKeywords = new List<string>();
        var scores = new Dictionary<IntentCategory, double>();

        // Check each pattern category with weighted scoring
        CheckPatternsWithWeight(lower, InsertPatterns, IntentCategory.Insert, scores, matchedKeywords);
        CheckPatternsWithWeight(lower, UpdatePatterns, IntentCategory.Update, scores, matchedKeywords);
        CheckPatternsWithWeight(lower, IndexPatterns, IntentCategory.DdlIndex, scores, matchedKeywords);
        CheckPatternsWithWeight(lower, ProcedurePatterns, IntentCategory.DdlProcedure, scores, matchedKeywords);
        CheckPatternsWithWeight(lower, AlterPatterns, IntentCategory.DdlAlter, scores, matchedKeywords);
        CheckPatternsWithWeight(lower, ViewPatterns, IntentCategory.DdlView, scores, matchedKeywords);
        CheckPatternsWithWeight(lower, QueryPatterns, IntentCategory.Query, scores, matchedKeywords);

        // Find highest scoring intent
        if (scores.Count == 0)
        {
            // ✅ FIX 1: Higher default confidence (0.70) when no patterns match
            // This ensures ambiguous queries get processed as QUERY instead of triggering LLM
            return new IntentClassificationResult
            {
                Intent = IntentCategory.Query,
                Route = PipelineRoute.Query,
                Confidence = 0.70, // ✅ CHANGED from 0.50 to 0.70
                Reasoning = "No specific patterns matched, defaulting to QUERY intent",
                NormalizedQuery = question,
                Method = ClassificationMethod.RuleBased
            };
        }

        var topIntent = scores.OrderByDescending(x => x.Value).First();
        var confidence = topIntent.Value;

        var result = new IntentClassificationResult
        {
            Intent = topIntent.Key,
            Route = ResolveRoute(topIntent.Key),
            Confidence = confidence,
            Reasoning = $"Matched {matchedKeywords.Count} pattern(s): {string.Join(", ", matchedKeywords.Take(3))}",
            NormalizedQuery = question,
            Method = ClassificationMethod.RuleBased,
            MatchedKeywords = matchedKeywords
        };

        // CRITICAL FIX 2: Extract entities for Write/DDL operations to avoid empty DetectedEntities
        if (topIntent.Key == IntentCategory.Insert ||
            topIntent.Key == IntentCategory.Update ||
            topIntent.Key == IntentCategory.DdlAlter ||
            topIntent.Key == IntentCategory.DdlIndex)
        {
            result.DetectedEntities = ExtractEntitiesSimple(question);
            _logger.LogInformation("[IntentClassifier] Rule-based entity extraction: [{Entities}] for intent {Intent}",
                string.Join(", ", result.DetectedEntities), topIntent.Key);
        }

        return result;
    }

    /// <summary>
    /// REFACTORED: Weighted pattern matching
    /// Uses Regex with word boundaries and weighted scoring based on pattern importance
    /// </summary>
    private void CheckPatternsWithWeight(
        string lowerQuestion,
        (string Pattern, Regex Regex, double Weight)[] patterns,
        IntentCategory intent,
        Dictionary<IntentCategory, double> scores,
        List<string> matchedKeywords)
    {
        double totalWeight = 0;
        int matchCount = 0;

        foreach (var (pattern, regex, weight) in patterns)
        {
            if (regex.IsMatch(lowerQuestion))
            {
                matchCount++;
                totalWeight += weight;
                matchedKeywords.Add(pattern);
                _logger.LogDebug("[IntentClassifier] Pattern matched: '{Pattern}' (weight: {Weight}) → {Intent}",
                    pattern, weight, intent);
            }
        }

        if (matchCount > 0)
        {
            // REFACTORED: Use weighted average instead of arbitrary formula
            // Confidence = weighted average with bonus for multiple matches
            var baseConfidence = totalWeight / matchCount;
            var multiMatchBonus = Math.Min(matchCount * 0.05, 0.15); // Up to 15% bonus for multiple matches
            var confidence = Math.Min(baseConfidence + multiMatchBonus, 0.95);

            scores[intent] = confidence;
            _logger.LogDebug("[IntentClassifier] {Intent} weighted score: {Confidence} ({Count} matches, totalWeight: {Weight})",
                intent, confidence, matchCount, totalWeight);
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

    ## Complexity Scoring
    You must score the query's complexity (0.0 to 1.0):
    - 0.1-0.3: Simple lookup (""show all users"", ""count employees"")
    - 0.4-0.6: Medium (""users created in 2023 ordered by name"")
    - 0.7-1.0: Complex reasoning, requires comparison, correlation, complex aggregations, or multi-step logic (""compare revenue between Q1 and Q2 divided by regions"")

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

    Any request with intent to permanently delete data -> classify as FORBIDDEN:
    - Direct SQL: DELETE, DROP, TRUNCATE, PURGE
    - Natural language: ""delete records"", ""remove users"", ""clear table"", ""delete all""
    - Even if user says ""just testing"", ""demo only"" -> still FORBIDDEN

    ## Output Format - PURE JSON ONLY

    Return EXACTLY this format:
    {{
      ""intent"": ""QUERY|INSERT|UPDATE|DDL_INDEX|DDL_PROCEDURE|DDL_ALTER|DDL_VIEW|FORBIDDEN|OFF_TOPIC|UNKNOWN"",
      ""confidence"": 0.0,
      ""complexityScore"": 0.0,
      ""reason"": ""Brief explanation (max 1 sentence)"",
      ""normalized_query"": ""Standardized version resolving pronouns"",
      ""entities"": [""table or column names mentioned""],
      ""warnings"": [""warnings if any""],
      ""forbidden_reason"": null,
      ""safe_alternatives"": []
    }}

    For FORBIDDEN, also fill:
    ""forbidden_reason"": ""Specific reason why blocked"",
    ""safe_alternatives"": [""soft delete"", ""archive table"", ""inactive flag""]}}";
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

        // ✅ FIX 3: Route low-confidence Unknown to QUERY instead of REJECT
        if (confidence < MinimumConfidenceThreshold && intent != IntentCategory.Forbidden)
        {
            intent = IntentCategory.Unknown;
            route = PipelineRoute.Query; // ✅ CHANGED from Reject to Query
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
            ComplexityScore = llmResponse.ComplexityScore,
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

    /// <summary>
    /// CRITICAL FIX 2: Simple entity extraction for rule-based classification
    /// Extracts entity names from common patterns to avoid empty DetectedEntities
    /// </summary>
    private List<string> ExtractEntitiesSimple(string question)
    {
        var entities = new List<string>();
        var lower = question.ToLowerInvariant();

        // Pattern 1: INSERT/ADD patterns - "thêm/tạo/insert/add [entity]"
        var insertMatch = Regex.Match(question,
            @"\b(?:thêm|tạo\s+mới|insert\s+(?:into\s+)?|add\s+(?:new\s+)?)\s*(\w+)",
            RegexOptions.IgnoreCase);
        if (insertMatch.Success)
        {
            entities.Add(insertMatch.Groups[1].Value);
        }

        // Pattern 2: UPDATE patterns - "cập nhật/update/sửa [entity]"
        var updateMatch = Regex.Match(question,
            @"\b(?:cập\s+nhật|update|sửa|chỉnh\s+sửa)\s+(\w+)",
            RegexOptions.IgnoreCase);
        if (updateMatch.Success && !entities.Contains(updateMatch.Groups[1].Value))
        {
            entities.Add(updateMatch.Groups[1].Value);
        }

        // Pattern 3: DDL ALTER patterns - "thêm cột vào [table]" or "alter table [table]"
        var alterMatch = Regex.Match(question,
            @"\b(?:thêm\s+cột\s+(?:vào\s+)?|alter\s+table\s+)\s*(\w+)",
            RegexOptions.IgnoreCase);
        if (alterMatch.Success && !entities.Contains(alterMatch.Groups[1].Value))
        {
            entities.Add(alterMatch.Groups[1].Value);
        }

        // Pattern 4: CREATE INDEX patterns - "tạo index cho [table]" or "create index on [table]"
        var indexMatch = Regex.Match(question,
            @"\b(?:tạo\s+index\s+(?:cho\s+)?|create\s+index\s+(?:on\s+)?)\s*(\w+)",
            RegexOptions.IgnoreCase);
        if (indexMatch.Success && !entities.Contains(indexMatch.Groups[1].Value))
        {
            entities.Add(indexMatch.Groups[1].Value);
        }

        // Fallback: Look for common entity keywords
        if (entities.Count == 0)
        {
            var commonEntities = new[] { "user", "customer", "product", "order", "employee", "supplier",
                                       "khachhang", "sanpham", "donhang", "nhanvien", "nguoidung" };

            foreach (var entity in commonEntities)
            {
                if (lower.Contains(entity))
                {
                    entities.Add(entity);
                    break; // Only take first match to avoid noise
                }
            }
        }

        _logger.LogDebug("[IntentClassifier] ExtractEntitiesSimple extracted: [{Entities}] from question: {Question}",
            string.Join(", ", entities), question);

        return entities;
    }
}

/// <summary>
/// LLM response model for intent classification
/// </summary>
internal class LlmClassificationResponse
{
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public double ComplexityScore { get; set; } = 0.0;
    public string Reasoning { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty; // Alias for Reasoning
    public string NormalizedQuery { get; set; } = string.Empty;
    public List<string> Entities { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? ForbiddenReason { get; set; }
}
