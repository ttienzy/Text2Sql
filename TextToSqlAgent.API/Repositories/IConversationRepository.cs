using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository interface for Conversation entity operations
/// </summary>
public interface IConversationRepository : IRepository<Conversation>
{
    /// <summary>
    /// Get all conversations for a specific user
    /// </summary>
    Task<IEnumerable<Conversation>> GetByUserIdAsync(string userId);

    /// <summary>
    /// Get conversation by ID for specific user (authorization check)
    /// </summary>
    Task<Conversation?> GetByIdAndUserIdAsync(string id, string userId);

    /// <summary>
    /// Get active (non-archived) conversations for user
    /// </summary>
    Task<IEnumerable<Conversation>> GetActiveConversationsAsync(string userId);

    /// <summary>
    /// Get conversations with message count
    /// </summary>
    Task<IEnumerable<Conversation>> GetConversationsWithMessageCountAsync(string userId);

    /// <summary>
    /// Update last active timestamp
    /// </summary>
    Task UpdateLastActiveAsync(string conversationId);

    /// <summary>
    /// Get conversations by connection ID
    /// </summary>
    Task<IEnumerable<Conversation>> GetByConnectionIdAsync(string connectionId, string userId);
}