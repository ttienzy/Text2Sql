using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Manages conversation context for multi-turn interactions
/// Enables follow-up questions and context carryover
/// </summary>
public class ConversationManager
{
    private readonly ILogger<ConversationManager> _logger;
    private readonly Dictionary<string, ConversationContext> _conversations = new();
    private readonly int _maxHistorySize;
    private readonly TimeSpan _conversationTimeout;

    public ConversationManager(
        ILogger<ConversationManager> logger,
        int maxHistorySize = 10,
        TimeSpan? conversationTimeout = null)
    {
        _logger = logger;
        _maxHistorySize = maxHistorySize;
        _conversationTimeout = conversationTimeout ?? TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Get or create conversation context
    /// </summary>
    public ConversationContext GetOrCreateContext(string? conversationId = null)
    {
        conversationId ??= Guid.NewGuid().ToString();

        if (_conversations.TryGetValue(conversationId, out var context))
        {
            // Check if conversation has timed out
            if (DateTime.UtcNow - context.LastActivityAt > _conversationTimeout)
            {
                _logger.LogInformation(
                    "[ConversationManager] Conversation {Id} timed out, creating new context",
                    conversationId);

                _conversations.Remove(conversationId);
                context = new ConversationContext { ConversationId = conversationId };
                _conversations[conversationId] = context;
            }
            else
            {
                context.LastActivityAt = DateTime.UtcNow;
            }

            return context;
        }

        context = new ConversationContext { ConversationId = conversationId };
        _conversations[conversationId] = context;

        _logger.LogInformation(
            "[ConversationManager] Created new conversation {Id}",
            conversationId);

        return context;
    }

    /// <summary>
    /// Add turn to conversation history
    /// </summary>
    public void AddTurn(
        ConversationContext context,
        string userQuestion,
        string systemResponse,
        string? sqlQuery = null,
        QueryIntent? intent = null,
        string? targetTable = null,
        bool success = true)
    {
        var turn = new ConversationTurn
        {
            TurnNumber = context.TurnCount + 1,
            UserQuestion = userQuestion,
            SystemResponse = systemResponse,
            SqlQuery = sqlQuery,
            Intent = intent,
            TargetTable = targetTable,
            Success = success
        };

        context.History.Add(turn);

        // Update context metadata
        if (!string.IsNullOrEmpty(sqlQuery))
        {
            context.LastSqlQuery = sqlQuery;
        }

        if (!string.IsNullOrEmpty(targetTable) && !context.RecentTables.Contains(targetTable))
        {
            context.RecentTables.Add(targetTable);
        }

        // Trim history if too long
        if (context.History.Count > _maxHistorySize)
        {
            context.History.RemoveAt(0);
        }

        _logger.LogDebug(
            "[ConversationManager] Added turn {Turn} to conversation {Id}",
            turn.TurnNumber,
            context.ConversationId);
    }

    /// <summary>
    /// Build context summary for LLM
    /// </summary>
    public string BuildContextSummary(ConversationContext context, int lastNTurns = 3)
    {
        if (context.History.Count == 0)
        {
            return "No previous conversation.";
        }

        var recentTurns = context.History
            .TakeLast(lastNTurns)
            .ToList();

        var summary = "Recent conversation:\n";

        foreach (var turn in recentTurns)
        {
            summary += $"\nTurn {turn.TurnNumber}:\n";
            summary += $"  User: {turn.UserQuestion}\n";

            if (!string.IsNullOrEmpty(turn.SqlQuery))
            {
                summary += $"  SQL: {turn.SqlQuery}\n";
            }

            summary += $"  Response: {turn.SystemResponse}\n";
        }

        // Add context metadata
        if (context.RecentTables.Any())
        {
            summary += $"\nRecently mentioned tables: {string.Join(", ", context.RecentTables)}\n";
        }

        return summary;
    }

    /// <summary>
    /// Detect if current question is a follow-up
    /// </summary>
    public bool IsFollowUpQuestion(ConversationContext context, string question)
    {
        if (context.History.Count == 0)
        {
            return false;
        }

        var lowerQuestion = question.ToLowerInvariant();

        // Follow-up indicators
        var followUpKeywords = new[]
        {
            // English
            "also", "too", "and", "what about", "how about",
            "same", "similar", "like that", "those", "them",
            "more", "other", "another", "previous", "last",
            
            // Vietnamese
            "cũng", "nữa", "thêm", "còn", "khác",
            "tương tự", "giống", "như vậy", "đó", "kia",
            "trước", "vừa rồi", "tiếp"
        };

        return followUpKeywords.Any(keyword => lowerQuestion.Contains(keyword));
    }

    /// <summary>
    /// Enrich question with context from previous turns
    /// </summary>
    public string EnrichQuestionWithContext(ConversationContext context, string question)
    {
        if (context.History.Count == 0 || !IsFollowUpQuestion(context, question))
        {
            return question;
        }

        var lastTurn = context.History.Last();
        var enriched = question;

        // ✅ PHASE 4: Structured context carryover

        // Add table context if missing
        if (!string.IsNullOrEmpty(lastTurn.TargetTable) &&
            !question.Contains(lastTurn.TargetTable, StringComparison.OrdinalIgnoreCase))
        {
            enriched = $"{question} (referring to {lastTurn.TargetTable} table)";
        }

        // Add filter context if available
        if (context.RecentFilters.Any())
        {
            var filterDesc = string.Join(", ", context.RecentFilters.Select(f =>
                $"{f.Field} {f.Operator} {f.Value}"));
            enriched += $" [previous filters: {filterDesc}]";
        }

        // Add recent tables context
        if (context.RecentTables.Any() && !ContainsTableReference(question))
        {
            enriched += $" [context tables: {string.Join(", ", context.RecentTables)}]";
        }

        _logger.LogDebug(
            "[ConversationManager] Enriched question: {Original} → {Enriched}",
            question,
            enriched);

        return enriched;
    }

    private bool ContainsTableReference(string question)
    {
        var lowerQuestion = question.ToLowerInvariant();
        return lowerQuestion.Contains("table") ||
               lowerQuestion.Contains("from") ||
               lowerQuestion.Contains("in");
    }

    /// <summary>
    /// Clear conversation context
    /// </summary>
    public void ClearContext(string conversationId)
    {
        if (_conversations.Remove(conversationId))
        {
            _logger.LogInformation(
                "[ConversationManager] Cleared conversation {Id}",
                conversationId);
        }
    }

    /// <summary>
    /// Get active conversation count
    /// </summary>
    public int GetActiveConversationCount()
    {
        // Clean up timed out conversations
        var timedOut = _conversations
            .Where(kvp => DateTime.UtcNow - kvp.Value.LastActivityAt > _conversationTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in timedOut)
        {
            _conversations.Remove(id);
        }

        return _conversations.Count;
    }
}
