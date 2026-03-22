using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Query classifier with two-layer classification:
/// 1. Rule-based (fast, handles 70-80% of queries)
/// 2. LLM-based fallback (for ambiguous cases)
/// </summary>
public class QueryClassifier : IQueryClassifier
{
    private readonly ILogger<QueryClassifier> _logger;
    private readonly ILLMQueryClassifier? _llmClassifier;

    // Complex indicators - if any present, classify as Complex
    private static readonly string[] ComplexKeywords =
    {
        "so sánh", "xu hướng", "phân tích", "tại sao", "dự báo", "tương quan",
        "nguyên nhân", "ảnh hưởng", "dự đoán", "cùng kỳ", "vì sao",
        "trend", "analysis", "compare", "forecast", "why", "reason",
        "tăng trưởng", "giảm", "biến động", "thay đổi"
    };

    // Medium indicators - if present (and not Complex), classify as Medium
    private static readonly string[] MediumKeywords =
    {
        "top", "tổng", "trung bình", "nhiều nhất", "ít nhất",
        "tháng", "quý", "năm", "theo từng", "mỗi", "nhóm theo",
        "tỷ lệ", "phần trăm", "sum", "average", "count", "max", "min",
        "hôm nay", "hôm qua", "tuần này", "tuần trước",
        "đầu tiên", "cuối cùng", "thứ hạng", "xếp hạng"
    };

    // Confidence threshold below which LLM fallback is triggered
    private const double ConfidenceThreshold = 0.8;

    public QueryClassifier(
        ILogger<QueryClassifier> logger,
        ILLMQueryClassifier? llmClassifier = null)
    {
        _logger = logger;
        _llmClassifier = llmClassifier;
    }

    public async Task<QueryClassifierResult> ClassifyAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryClassifierResult
            {
                Complexity = QueryComplexity.Simple,
                Confidence = 1.0,
                Reasoning = "Empty query - defaulting to Simple",
                Method = ClassificationMethod.RuleBased
            };
        }

        // Layer 1: Rule-based classification (fast, no LLM)
        var ruleBasedResult = ClassifyByRules(query);

        _logger.LogDebug(
            "[QueryClassifier] Rule-based classification: {Complexity} (confidence: {Confidence}) - {Keywords}",
            ruleBasedResult.Complexity,
            ruleBasedResult.Confidence,
            string.Join(", ", ruleBasedResult.MatchedKeywords));

        // If high confidence, return immediately (no LLM needed)
        if (ruleBasedResult.Confidence >= ConfidenceThreshold)
        {
            _logger.LogInformation(
                "[QueryClassifier] High confidence ({Confidence}), using rule-based classification: {Complexity}",
                ruleBasedResult.Confidence,
                ruleBasedResult.Complexity);

            return ruleBasedResult;
        }

        // Layer 2: LLM fallback for uncertain cases
        if (_llmClassifier != null && RequiresLLMClassification(ruleBasedResult))
        {
            _logger.LogInformation(
                "[QueryClassifier] Low confidence ({Confidence}), triggering LLM classification",
                ruleBasedResult.Confidence);

            try
            {
                var llmResult = await _llmClassifier.ClassifyAsync(query, ct);

                // Use LLM result if it has higher confidence
                if (llmResult.Confidence > ruleBasedResult.Confidence)
                {
                    _logger.LogInformation(
                        "[QueryClassifier] LLM classification overrides: {Complexity} (confidence: {Confidence})",
                        llmResult.Complexity,
                        llmResult.Confidence);

                    return llmResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QueryClassifier] LLM classification failed, using rule-based result");
            }
        }

        // Conservative: if uncertain, classify as Complex
        if (ruleBasedResult.Confidence < 0.5)
        {
            _logger.LogWarning(
                "[QueryClassifier] Low confidence ({Confidence}), defaulting to Complex for safety",
                ruleBasedResult.Confidence);

            return new QueryClassifierResult
            {
                Complexity = QueryComplexity.Complex,
                Confidence = 0.5,
                Reasoning = $"Low confidence - conservative classification as Complex. Original: {ruleBasedResult.Reasoning}",
                Method = ruleBasedResult.Method,
                MatchedKeywords = ruleBasedResult.MatchedKeywords,
                UsedLLM = ruleBasedResult.UsedLLM
            };
        }

        return ruleBasedResult;
    }

    public bool RequiresLLMClassification(QueryClassifierResult preliminary)
    {
        // Requires LLM if confidence is below threshold
        return preliminary.Confidence < ConfidenceThreshold;
    }

    private QueryClassifierResult ClassifyByRules(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        var matchedComplex = new List<string>();
        var matchedMedium = new List<string>();

        // Check for Complex indicators first (highest priority)
        foreach (var keyword in ComplexKeywords)
        {
            if (lowerQuery.Contains(keyword.ToLowerInvariant()))
            {
                matchedComplex.Add(keyword);
            }
        }

        if (matchedComplex.Count > 0)
        {
            return new QueryClassifierResult
            {
                Complexity = QueryComplexity.Complex,
                Confidence = 0.95,
                Reasoning = $"Found {matchedComplex.Count} complex indicator(s): {string.Join(", ", matchedComplex)}",
                Method = ClassificationMethod.RuleBased,
                MatchedKeywords = matchedComplex
            };
        }

        // Check for Medium indicators
        foreach (var keyword in MediumKeywords)
        {
            if (lowerQuery.Contains(keyword.ToLowerInvariant()))
            {
                matchedMedium.Add(keyword);
            }
        }

        if (matchedMedium.Count > 0)
        {
            // Higher confidence with more keywords
            var confidence = Math.Min(0.9, 0.6 + (matchedMedium.Count * 0.1));

            return new QueryClassifierResult
            {
                Complexity = QueryComplexity.Medium,
                Confidence = confidence,
                Reasoning = $"Found {matchedMedium.Count} medium indicator(s): {string.Join(", ", matchedMedium)}",
                Method = ClassificationMethod.RuleBased,
                MatchedKeywords = matchedMedium
            };
        }

        // Default: Simple (no keywords matched)
        return new QueryClassifierResult
        {
            Complexity = QueryComplexity.Simple,
            Confidence = 0.7,
            Reasoning = "No complex or medium indicators found - defaulting to Simple",
            Method = ClassificationMethod.RuleBased,
            MatchedKeywords = new List<string>()
        };
    }
}
