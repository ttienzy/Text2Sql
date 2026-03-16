using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository interface for Message entity operations
/// </summary>
public interface IMessageRepository : IRepository<Message>
{
    /// <summary>
    /// Get messages for a specific conversation with pagination
    /// </summary>
    Task<IEnumerable<Message>> GetByConversationIdAsync(string conversationId, int skip = 0, int take = 50);

    /// <summary>
    /// Get all messages for a specific conversation
    /// </summary>
    Task<IEnumerable<Message>> GetByConversationIdAsync(string conversationId);

    /// <summary>
    /// Get message count for a conversation
    /// </summary>
    Task<int> GetMessageCountAsync(string conversationId);

    /// <summary>
    /// Get recent messages for conversation context
    /// </summary>
    Task<IEnumerable<Message>> GetRecentMessagesAsync(string conversationId, int count = 10);

    /// <summary>
    /// Get messages by user ID (for analytics)
    /// </summary>
    Task<IEnumerable<Message>> GetByUserIdAsync(string userId, int page = 1, int pageSize = 50);

    /// <summary>
    /// Get messages with SQL queries (for analysis)
    /// </summary>
    Task<IEnumerable<Message>> GetMessagesWithSqlAsync(string conversationId);

    /// <summary>
    /// Get recent SQL queries for a user
    /// </summary>
    Task<IEnumerable<Message>> GetRecentQueriesAsync(string userId, int limit = 5);
}