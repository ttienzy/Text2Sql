namespace TextToSqlAgent.Core.Agent;

/// <summary>
/// Enhanced agent request with conversation context
/// </summary>
public class ConversationAwareAgentRequest : AgentRequest
{
    public string? ConversationId { get; set; }
    public List<ConversationMessage> ConversationHistory { get; set; } = new();
    public Dictionary<string, object> ConversationContext { get; set; } = new();
    public bool IsFollowUpQuestion { get; set; }
    public string? PreviousQuery { get; set; }
    public string? PreviousResult { get; set; }

    public ConversationAwareAgentRequest(string question, string? databaseId = null, string? conversationId = null)
        : base(question, databaseId)
    {
        ConversationId = conversationId;
    }

    /// <summary>
    /// Add message to conversation history
    /// </summary>
    public void AddMessage(string role, string content, string? sqlQuery = null, object? result = null)
    {
        ConversationHistory.Add(new ConversationMessage
        {
            Role = role,
            Content = content,
            SqlQuery = sqlQuery,
            Result = result,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get conversation context as formatted string for LLM
    /// </summary>
    public string GetConversationContextForLLM()
    {
        if (!ConversationHistory.Any())
            return string.Empty;

        var context = "# CONVERSATION HISTORY\n\n";

        foreach (var message in ConversationHistory.TakeLast(10)) // Last 10 messages
        {
            context += $"**{message.Role.ToUpper()}**: {message.Content}\n";

            if (!string.IsNullOrEmpty(message.SqlQuery))
            {
                context += $"```sql\n{message.SqlQuery}\n```\n";
            }

            if (message.Result != null)
            {
                context += $"*Result: {GetResultSummary(message.Result)}*\n";
            }

            context += "\n";
        }

        return context;
    }

    /// <summary>
    /// Check if current question is related to previous conversation
    /// </summary>
    public bool IsRelatedToPreviousContext()
    {
        if (!ConversationHistory.Any())
            return false;

        // Simple heuristics - can be enhanced with NLP
        var currentQuestion = Question.ToLower();
        var lastUserMessage = ConversationHistory.LastOrDefault(m => m.Role == "user")?.Content?.ToLower();

        if (string.IsNullOrEmpty(lastUserMessage))
            return false;

        // Check for follow-up indicators
        var followUpIndicators = new[]
        {
            "also", "and", "what about", "how about", "can you also",
            "show me more", "expand", "detail", "breakdown",
            "that", "this", "these", "those", "it", "them"
        };

        return followUpIndicators.Any(indicator => currentQuestion.Contains(indicator));
    }

    private static string GetResultSummary(object result)
    {
        if (result == null) return "No result";

        // Handle different result types
        if (result is IEnumerable<object> enumerable)
        {
            var count = enumerable.Count();
            return $"{count} row(s) returned";
        }

        return result.ToString() ?? "Result available";
    }
}

/// <summary>
/// Conversation message for history tracking
/// </summary>
public class ConversationMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public string? SqlQuery { get; set; }
    public object? Result { get; set; }
    public DateTime Timestamp { get; set; }
}