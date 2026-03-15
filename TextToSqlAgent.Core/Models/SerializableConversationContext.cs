using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Models;

/// <summary>
/// Serializable conversation context for persistence
/// Stores context between requests for multi-turn interactions
/// </summary>
public class SerializableConversationContext
{
    /// <summary>
    /// Tables mentioned in this conversation
    /// </summary>
    [JsonPropertyName("mentionedTables")]
    public List<string> MentionedTables { get; set; } = new();

    /// <summary>
    /// User-defined aliases (e.g., "tháng này" = current month date range)
    /// </summary>
    [JsonPropertyName("aliases")]
    public Dictionary<string, string> Aliases { get; set; } = new();

    /// <summary>
    /// Last SQL query generated
    /// </summary>
    [JsonPropertyName("lastSql")]
    public string? LastSql { get; set; }

    /// <summary>
    /// Summary of last query result
    /// </summary>
    [JsonPropertyName("lastResultSummary")]
    public string? LastResultSummary { get; set; }

    /// <summary>
    /// User preferences discovered during conversation
    /// </summary>
    [JsonPropertyName("userPreferences")]
    public Dictionary<string, string> UserPreferences { get; set; } = new();

    /// <summary>
    /// Last active timestamp
    /// </summary>
    [JsonPropertyName("lastActiveAt")]
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Extension methods for SerializableConversationContext
/// </summary>
public static class SerializableConversationContextExtensions
{
    /// <summary>
    /// Convert to display string for system prompt injection
    /// </summary>
    public static string ToSystemPromptContext(this SerializableConversationContext? context)
    {
        if (context == null)
        {
            return "No previous conversation.";
        }

        var parts = new List<string>();

        // Add mentioned tables
        if (context.MentionedTables.Any())
        {
            parts.Add($"Các bảng đã đề cập: {string.Join(", ", context.MentionedTables)}");
        }

        // Add last SQL
        if (!string.IsNullOrEmpty(context.LastSql))
        {
            parts.Add($"SQL cuối: {context.LastSql}");
        }

        // Add aliases
        if (context.Aliases.Any())
        {
            var aliasParts = context.Aliases.Select(kvp => $"'{kvp.Key}' = {kvp.Value}");
            parts.Add($"Alias người dùng dùng: {string.Join(", ", aliasParts)}");
        }

        // Add user preferences
        if (context.UserPreferences.Any())
        {
            var prefParts = context.UserPreferences.Select(kvp => $"{kvp.Key}: {kvp.Value}");
            parts.Add($"Tùy chọn người dùng: {string.Join(", ", prefParts)}");
        }

        return parts.Any()
            ? string.Join("\n", parts)
            : "No previous conversation.";
    }

    /// <summary>
    /// Create a new context with updated values
    /// </summary>
    public static SerializableConversationContext AddMentionedTable(
        this SerializableConversationContext context,
        string tableName)
    {
        if (!string.IsNullOrEmpty(tableName) && !context.MentionedTables.Contains(tableName))
        {
            context.MentionedTables.Add(tableName);
            // Keep only last 10 tables
            if (context.MentionedTables.Count > 10)
            {
                context.MentionedTables.RemoveAt(0);
            }
        }
        context.LastActiveAt = DateTime.UtcNow;
        return context;
    }

    /// <summary>
    /// Add alias to context
    /// </summary>
    public static SerializableConversationContext AddAlias(
        this SerializableConversationContext context,
        string alias,
        string value)
    {
        if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(value))
        {
            context.Aliases[alias] = value;
        }
        context.LastActiveAt = DateTime.UtcNow;
        return context;
    }

    /// <summary>
    /// Update last SQL query
    /// </summary>
    public static SerializableConversationContext UpdateLastSql(
        this SerializableConversationContext context,
        string sql,
        string? resultSummary = null)
    {
        context.LastSql = sql;
        context.LastResultSummary = resultSummary;
        context.LastActiveAt = DateTime.UtcNow;
        return context;
    }
}
