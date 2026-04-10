using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Entities;
using System.Text.RegularExpressions;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Extracts conversation context for better query understanding
/// Handles entity extraction, pronoun resolution, and cross-turn references
/// </summary>
public class ConversationContextExtractor
{
    private readonly ILogger<ConversationContextExtractor> _logger;

    // Pronouns and references that need resolution
    private static readonly string[] ReferenceKeywords = new[]
    {
        "đó", "này", "those", "these", "that", "this",
        "họ", "chúng", "they", "them", "it",
        "cùng", "same", "similar", "tương tự"
    };

    // Temporal references
    private static readonly string[] TemporalKeywords = new[]
    {
        "trước", "sau", "previous", "next", "last", "recent",
        "gần đây", "vừa rồi", "recently", "lately"
    };

    public ConversationContextExtractor(ILogger<ConversationContextExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract comprehensive conversation context from history
    /// </summary>
    public ExtractedConversationContext ExtractContext(
        string currentQuery,
        List<Message> history)
    {
        _logger.LogDebug("[ConversationExtractor] Extracting context from {Count} messages", history.Count);

        var context = new ExtractedConversationContext
        {
            CurrentQuery = currentQuery,
            HasReferences = ContainsReferences(currentQuery)
        };

        if (!history.Any())
        {
            _logger.LogDebug("[ConversationExtractor] No history available");
            return context;
        }

        // Take last 5 messages for context
        var recentMessages = history.TakeLast(5).ToList();

        // 1. Extract mentioned entities (tables, columns, values)
        context.MentionedTables = ExtractTableNames(recentMessages);
        context.MentionedColumns = ExtractColumnNames(recentMessages);
        context.MentionedValues = ExtractValues(recentMessages);

        // 2. Resolve pronouns and references
        if (context.HasReferences)
        {
            context.PronounResolutions = ResolvePronounsInQuery(currentQuery, recentMessages);
        }

        // 3. Track query intent evolution
        context.IntentChain = recentMessages
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => ExtractIntentFromMessage(m))
            .ToList();

        // 4. Extract filters from previous queries
        context.PreviousFilters = ExtractFiltersFromHistory(recentMessages);

        // 5. Build conversation summary
        context.ConversationSummary = BuildConversationSummary(recentMessages);

        _logger.LogInformation(
            "[ConversationExtractor] Extracted context: {Tables} tables, {Columns} columns, {Resolutions} resolutions",
            context.MentionedTables.Count,
            context.MentionedColumns.Count,
            context.PronounResolutions.Count);

        return context;
    }

    /// <summary>
    /// Check if query contains references that need resolution
    /// </summary>
    private bool ContainsReferences(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        return ReferenceKeywords.Any(k => lowerQuery.Contains(k)) ||
               TemporalKeywords.Any(k => lowerQuery.Contains(k));
    }

    /// <summary>
    /// Extract table names mentioned in conversation
    /// </summary>
    private List<string> ExtractTableNames(List<Message> messages)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            // Look for capitalized words that might be table names
            var matches = Regex.Matches(message.Content, @"\b[A-Z][a-z]+(?:[A-Z][a-z]+)*\b");
            foreach (Match match in matches)
            {
                tables.Add(match.Value);
            }

            // Look for common table name patterns
            var tablePatterns = new[]
            {
                @"from\s+(\w+)",
                @"join\s+(\w+)",
                @"table\s+(\w+)",
                @"bảng\s+(\w+)"
            };

            foreach (var pattern in tablePatterns)
            {
                var patternMatches = Regex.Matches(
                    message.Content,
                    pattern,
                    RegexOptions.IgnoreCase);

                foreach (Match match in patternMatches)
                {
                    if (match.Groups.Count > 1)
                    {
                        tables.Add(match.Groups[1].Value);
                    }
                }
            }
        }

        return tables.ToList();
    }

    /// <summary>
    /// Extract column names mentioned in conversation
    /// </summary>
    private List<string> ExtractColumnNames(List<Message> messages)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            // Look for column patterns
            var columnPatterns = new[]
            {
                @"column\s+(\w+)",
                @"field\s+(\w+)",
                @"cột\s+(\w+)",
                @"trường\s+(\w+)"
            };

            foreach (var pattern in columnPatterns)
            {
                var matches = Regex.Matches(
                    message.Content,
                    pattern,
                    RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        columns.Add(match.Groups[1].Value);
                    }
                }
            }
        }

        return columns.ToList();
    }

    /// <summary>
    /// Extract specific values mentioned in conversation
    /// </summary>
    private List<string> ExtractValues(List<Message> messages)
    {
        var values = new HashSet<string>();

        foreach (var message in messages)
        {
            // Look for quoted strings
            var quotedMatches = Regex.Matches(message.Content, @"['""]([^'""]+)['""]");
            foreach (Match match in quotedMatches)
            {
                if (match.Groups.Count > 1)
                {
                    values.Add(match.Groups[1].Value);
                }
            }

            // Look for numbers
            var numberMatches = Regex.Matches(message.Content, @"\b\d+(?:\.\d+)?\b");
            foreach (Match match in numberMatches)
            {
                values.Add(match.Value);
            }
        }

        return values.ToList();
    }

    /// <summary>
    /// Resolve pronouns and references in current query
    /// </summary>
    private Dictionary<string, string> ResolvePronounsInQuery(
        string currentQuery,
        List<Message> history)
    {
        var resolutions = new Dictionary<string, string>();
        var lowerQuery = currentQuery.ToLowerInvariant();

        // "các khách hàng đó" / "those customers" → resolve to previous query's customer filter
        if (lowerQuery.Contains("đó") || lowerQuery.Contains("those") || lowerQuery.Contains("that"))
        {
            var lastUserQuery = history
                .Where(m => m.Role == "user")
                .LastOrDefault();

            if (lastUserQuery != null)
            {
                resolutions["reference"] = $"Referring to: {lastUserQuery.Content}";
            }

            // Try to extract filter from last SQL query
            var lastAssistantMsg = history
                .Where(m => m.Role == "assistant" && !string.IsNullOrEmpty(m.SqlQuery))
                .LastOrDefault();

            if (lastAssistantMsg?.SqlQuery != null)
            {
                var filter = ExtractFilterFromSql(lastAssistantMsg.SqlQuery);
                if (!string.IsNullOrEmpty(filter))
                {
                    resolutions["filter"] = filter;
                }
            }
        }

        // "cùng loại" / "same type" → resolve to previous category/type
        if (lowerQuery.Contains("cùng") || lowerQuery.Contains("same") || lowerQuery.Contains("similar"))
        {
            var previousValues = ExtractValues(history);
            if (previousValues.Any())
            {
                resolutions["same_as"] = string.Join(", ", previousValues.Take(3));
            }
        }

        // Temporal references: "trước đó" / "previous"
        if (TemporalKeywords.Any(k => lowerQuery.Contains(k)))
        {
            var lastQuery = history
                .Where(m => m.Role == "user")
                .LastOrDefault();

            if (lastQuery != null)
            {
                resolutions["temporal_reference"] = $"Previous query: {lastQuery.Content}";
            }
        }

        return resolutions;
    }

    /// <summary>
    /// Extract WHERE clause filter from SQL
    /// </summary>
    private string ExtractFilterFromSql(string sql)
    {
        try
        {
            var whereMatch = Regex.Match(
                sql,
                @"WHERE\s+(.+?)(?:GROUP BY|ORDER BY|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (whereMatch.Success && whereMatch.Groups.Count > 1)
            {
                return whereMatch.Groups[1].Value.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ConversationExtractor] Failed to extract filter from SQL");
        }

        return "";
    }

    /// <summary>
    /// Extract intent from message content
    /// </summary>
    private string ExtractIntentFromMessage(Message message)
    {
        var content = message.Content.ToLowerInvariant();

        if (content.Contains("hiển thị") || content.Contains("show") || content.Contains("list"))
            return "list";

        if (content.Contains("đếm") || content.Contains("count") || content.Contains("how many"))
            return "count";

        if (content.Contains("tổng") || content.Contains("sum") || content.Contains("total"))
            return "aggregate";

        if (content.Contains("so sánh") || content.Contains("compare"))
            return "compare";

        if (content.Contains("xu hướng") || content.Contains("trend"))
            return "trend";

        return "query";
    }

    /// <summary>
    /// Extract filters from conversation history
    /// </summary>
    private List<string> ExtractFiltersFromHistory(List<Message> messages)
    {
        var filters = new List<string>();

        foreach (var message in messages.Where(m => !string.IsNullOrEmpty(m.SqlQuery)))
        {
            var filter = ExtractFilterFromSql(message.SqlQuery!);
            if (!string.IsNullOrEmpty(filter))
            {
                filters.Add(filter);
            }
        }

        return filters;
    }

    /// <summary>
    /// Build a concise conversation summary
    /// </summary>
    private string BuildConversationSummary(List<Message> messages)
    {
        if (!messages.Any())
            return "";

        var userQueries = messages
            .Where(m => m.Role == "user")
            .Select(m => m.Content)
            .ToList();

        if (!userQueries.Any())
            return "";

        return $"User asked {userQueries.Count} questions about: " +
               string.Join(", ", userQueries.Take(3).Select(q =>
                   q.Length > 50 ? q.Substring(0, 47) + "..." : q));
    }
}

/// <summary>
/// Conversation context extracted from history
/// </summary>
public class ExtractedConversationContext
{
    public string CurrentQuery { get; set; } = "";
    public bool HasReferences { get; set; }
    public List<string> MentionedTables { get; set; } = new();
    public List<string> MentionedColumns { get; set; } = new();
    public List<string> MentionedValues { get; set; } = new();
    public Dictionary<string, string> PronounResolutions { get; set; } = new();
    public List<string> IntentChain { get; set; } = new();
    public List<string> PreviousFilters { get; set; } = new();
    public string ConversationSummary { get; set; } = "";

    /// <summary>
    /// Format context for inclusion in LLM prompt
    /// </summary>
    public string FormatForPrompt()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(ConversationSummary))
        {
            parts.Add($"Conversation: {ConversationSummary}");
        }

        if (MentionedTables.Any())
        {
            parts.Add($"Previously mentioned tables: {string.Join(", ", MentionedTables)}");
        }

        if (PronounResolutions.Any())
        {
            parts.Add("References:");
            foreach (var resolution in PronounResolutions)
            {
                parts.Add($"  - {resolution.Key}: {resolution.Value}");
            }
        }

        if (PreviousFilters.Any())
        {
            parts.Add($"Previous filters: {string.Join("; ", PreviousFilters.Take(2))}");
        }

        return string.Join("\n", parts);
    }
}
