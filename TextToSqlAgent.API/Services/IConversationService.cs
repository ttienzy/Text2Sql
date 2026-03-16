using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for managing conversations
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// Get conversations for a user with pagination
    /// </summary>
    Task<IEnumerable<Conversation>> GetUserConversationsAsync(string userId, int skip = 0, int take = 50, bool includeArchived = false);

    /// <summary>
    /// Get a conversation by ID for a specific user
    /// </summary>
    Task<Conversation?> GetConversationAsync(string id, string userId);

    /// <summary>
    /// Create a new conversation
    /// </summary>
    Task<Conversation> CreateConversationAsync(string userId, string connectionId, string title, string? contextJson = null);

    /// <summary>
    /// Update conversation title and context
    /// </summary>
    Task<Conversation?> UpdateConversationAsync(string id, string userId, string title, string? contextJson = null);

    /// <summary>
    /// Archive a conversation
    /// </summary>
    Task<bool> ArchiveConversationAsync(string id, string userId);

    /// <summary>
    /// Delete a conversation and all its messages
    /// </summary>
    Task<bool> DeleteConversationAsync(string id, string userId);

    /// <summary>
    /// Get conversation with messages
    /// </summary>
    Task<Conversation?> GetConversationWithMessagesAsync(string id, string userId);

    /// <summary>
    /// Update conversation last active time
    /// </summary>
    Task UpdateLastActiveAsync(string id);
}