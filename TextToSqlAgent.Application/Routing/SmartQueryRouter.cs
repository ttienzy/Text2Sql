using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Entities;
using DatabaseSchema = TextToSqlAgent.Core.Models.DatabaseSchema;

namespace TextToSqlAgent.Application.Routing;

/// <summary>
/// Smart multi-stage query routing service with complexity analysis and schema-aware decisions
/// Enhances routing logic with semantic analysis and confidence-based escalation
/// </summary>
public class SmartQueryRouter
{
    private readonly ILogger<SmartQueryRouter> _logger;
    private readonly QueryComplexityAnalyzer? _complexityAnalyzer;
    private readonly IIntentClassifier _intentClassifier;

    public SmartQueryRouter(
        ILogger<SmartQueryRouter> logger,
        IIntentClassifier intentClassifier,
        QueryComplexityAnalyzer? complexityAnalyzer = null)
    {
        _logger = logger;
        _intentClassifier = intentClassifier;
        _complexityAnalyzer = complexityAnalyzer;
    }

    /// <summary>
    /// Determine optimal query complexity with multi-stage analysis
    /// </summary>
    public async Task<SmartRoutingDecision> DetermineComplexityAsync(
        string question,
        DatabaseSchema schema,
        List<Message>? conversationHistory = null,
        QueryComplexity? previousComplexity = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[SmartRouter] Analyzing query for routing decision");

        // Stage 1: Intent Classification
        var conversationHistoryStr = conversationHistory != null && conversationHistory.Any()
            ? string.Join("\n", conversationHistory.Select(m => $"{m.Role}: {m.Content}"))
            : null;

        var intent = await _intentClassifier.ClassifyAsync(
            question,
            conversationHistoryStr,
            null, // databaseContext
            cancellationToken);

        _logger.LogInformation(
            "[SmartRouter] Intent: {Intent}, Route: {Route}, Confidence: {Confidence}",
            intent.Intent,
            intent.Route,
            intent.Confidence);

        // Stage 2: Forbidden check (highest priority)
        if (intent.Intent == IntentCategory.Forbidden)
        {
            return new SmartRoutingDecision
            {
                RecommendedComplexity = QueryComplexity.Simple, // Doesn't matter for forbidden
                Confidence = 1.0,
                Reasoning = "Forbidden query detected",
                IsForbidden = true,
                RequiredTables = new List<string>(),
                EstimatedLlmCalls = 0
            };
        }

        // Stage 3: Complexity Analysis (if available)
        ComplexityScore? complexityScore = null;
        if (_complexityAnalyzer != null)
        {
            complexityScore = await _complexityAnalyzer.AnalyzeAsync(
                question,
                schema,
                conversationHistory,
                cancellationToken);

            _logger.LogInformation(
                "[SmartRouter] Complexity: {Level}, Confidence: {Confidence}, Reasoning: {Reasoning}",
                complexityScore.Level,
                complexityScore.Confidence,
                complexityScore.Reasoning);
        }

        // Stage 4: Multi-stage routing decision
        return MakeRoutingDecision(intent, complexityScore, schema, previousComplexity);
    }

    private SmartRoutingDecision MakeRoutingDecision(
        IntentClassificationResult intent,
        ComplexityScore? complexityScore,
        DatabaseSchema schema,
        QueryComplexity? previousComplexity)
    {
        // Escalation logic: If retrying from a previous complexity, escalate
        if (previousComplexity.HasValue)
        {
            var escalated = EscalateComplexity(previousComplexity.Value);
            _logger.LogInformation(
                "[SmartRouter] Escalating from {Previous} to {New}",
                previousComplexity.Value,
                escalated);

            return new SmartRoutingDecision
            {
                RecommendedComplexity = escalated,
                Confidence = 0.7,
                Reasoning = $"Escalated from {previousComplexity.Value} due to previous failure",
                RequiredTables = complexityScore?.RequiredTables ?? new List<string>(),
                EstimatedLlmCalls = GetEstimatedLlmCalls(escalated)
            };
        }

        // Use complexity analyzer if available
        if (complexityScore != null)
        {
            return MakeDecisionFromComplexity(complexityScore, intent, schema);
        }

        // Fallback to intent-based routing
        return MakeDecisionFromIntent(intent);
    }

    private SmartRoutingDecision MakeDecisionFromComplexity(
        ComplexityScore complexity,
        IntentClassificationResult intent,
        DatabaseSchema schema)
    {
        // High confidence simple query
        if (complexity.Level == ComplexityLevel.Simple && complexity.Confidence > 0.8)
        {
            return new SmartRoutingDecision
            {
                RecommendedComplexity = QueryComplexity.Simple,
                Confidence = complexity.Confidence,
                Reasoning = $"High confidence simple query: {complexity.Reasoning}",
                RequiredTables = complexity.RequiredTables,
                EstimatedLlmCalls = complexity.EstimatedLlmCalls
            };
        }

        // High confidence complex query
        if (complexity.Level == ComplexityLevel.Complex && complexity.Confidence > 0.7)
        {
            return new SmartRoutingDecision
            {
                RecommendedComplexity = QueryComplexity.Complex,
                Confidence = complexity.Confidence,
                Reasoning = $"High confidence complex query: {complexity.Reasoning}",
                RequiredTables = complexity.RequiredTables,
                EstimatedLlmCalls = complexity.EstimatedLlmCalls
            };
        }

        // Schema-aware decision: Multiple tables → Complex
        if (complexity.RequiredTables?.Count > 3)
        {
            return new SmartRoutingDecision
            {
                RecommendedComplexity = QueryComplexity.Complex,
                Confidence = 0.85,
                Reasoning = $"Multiple tables required ({complexity.RequiredTables.Count}): {string.Join(", ", complexity.RequiredTables)}",
                RequiredTables = complexity.RequiredTables,
                EstimatedLlmCalls = 6
            };
        }

        // Temporal patterns or comparisons → Medium or Complex
        if (complexity.Reasoning?.Contains("temporal", StringComparison.OrdinalIgnoreCase) == true ||
            complexity.Reasoning?.Contains("comparison", StringComparison.OrdinalIgnoreCase) == true)
        {
            var targetComplexity = complexity.RequiredTables?.Count > 2
                ? QueryComplexity.Complex
                : QueryComplexity.Medium;

            return new SmartRoutingDecision
            {
                RecommendedComplexity = targetComplexity,
                Confidence = complexity.Confidence,
                Reasoning = $"Temporal/comparison pattern detected: {complexity.Reasoning}",
                RequiredTables = complexity.RequiredTables,
                EstimatedLlmCalls = complexity.EstimatedLlmCalls
            };
        }

        // Default to Medium for moderate complexity
        if (complexity.Level == ComplexityLevel.Medium)
        {
            return new SmartRoutingDecision
            {
                RecommendedComplexity = QueryComplexity.Medium,
                Confidence = complexity.Confidence,
                Reasoning = $"Moderate complexity: {complexity.Reasoning}",
                RequiredTables = complexity.RequiredTables,
                EstimatedLlmCalls = complexity.EstimatedLlmCalls
            };
        }

        // Low confidence → Use intent-based fallback
        _logger.LogInformation(
            "[SmartRouter] Low complexity confidence ({Confidence}), falling back to intent-based routing",
            complexity.Confidence);
        return MakeDecisionFromIntent(intent);
    }

    private SmartRoutingDecision MakeDecisionFromIntent(IntentClassificationResult intent)
    {
        // Map based on complexity score if available
        QueryComplexity complexity;

        if (intent.ComplexityScore >= 0.7)
        {
            complexity = QueryComplexity.Complex;
        }
        else if (intent.ComplexityScore >= 0.4)
        {
            complexity = QueryComplexity.Medium;
        }
        else
        {
            complexity = QueryComplexity.Simple;
        }

        return new SmartRoutingDecision
        {
            RecommendedComplexity = complexity,
            Confidence = intent.Confidence,
            Reasoning = $"Intent-based routing: {intent.Intent}, Complexity score: {intent.ComplexityScore}",
            RequiredTables = new List<string>(),
            EstimatedLlmCalls = GetEstimatedLlmCalls(complexity)
        };
    }

    private QueryComplexity EscalateComplexity(QueryComplexity current)
    {
        return current switch
        {
            QueryComplexity.Simple => QueryComplexity.Medium,
            QueryComplexity.Medium => QueryComplexity.Complex,
            QueryComplexity.Complex => QueryComplexity.Complex, // Already at max
            _ => QueryComplexity.Medium
        };
    }

    private int GetEstimatedLlmCalls(QueryComplexity complexity)
    {
        return complexity switch
        {
            QueryComplexity.Simple => 2,
            QueryComplexity.Medium => 4,
            QueryComplexity.Complex => 6,
            _ => 3
        };
    }
}

/// <summary>
/// Smart routing decision with confidence and reasoning
/// </summary>
public class SmartRoutingDecision
{
    public QueryComplexity RecommendedComplexity { get; set; }
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = "";
    public bool IsForbidden { get; set; }
    public List<string> RequiredTables { get; set; } = new();
    public int EstimatedLlmCalls { get; set; }
}
