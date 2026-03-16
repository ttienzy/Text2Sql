using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service for managing conversations
/// </summary>
public class ConversationService : IConversationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IUnitOfWork unitOfWork,
        ILogger<ConversationService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Get conversations for a user with pagination
    /// </summary>
    public async Task<IEnumerable<Conversation>> GetUserConversationsAsync(string userId, int skip = 0, int take = 50, bool includeArchived = false)
    {
        try
        {
            var conversations = await _unitOfWork.Conversations.GetByUserIdAsync(userId);

            // Apply filtering and pagination in memory for now
            // In a real implementation, this should be done at the database level
            var filteredConversations = conversations.AsEnumerable();

            if (!includeArchived)
            {
                filteredConversations = filteredConversations.Where(c => !c.IsArchived);
            }

            return filteredConversations
                .OrderByDescending(c => c.LastActiveAt)
                .Skip(skip)
                .Take(take);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversations for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Get a conversation by ID for a specific user
    /// </summary>
    public async Task<Conversation?> GetConversationAsync(string id, string userId)
    {
        try
        {
            return await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation {ConversationId} for user {UserId}", id, userId);
            throw;
        }
    }

    /// <summary>
    /// Create a new conversation
    /// </summary>
    public async Task<Conversation> CreateConversationAsync(string userId, string connectionId, string title, string? contextJson = null)
    {
        try
        {
            var conversation = new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ConnectionId = connectionId,
                Title = title,
                ContextJson = contextJson,
                CreatedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow,
                IsArchived = false
            };

            await _unitOfWork.Conversations.AddAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Created conversation {ConversationId} for user {UserId}", conversation.Id, userId);
            return conversation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Update conversation title and context
    /// </summary>
    public async Task<Conversation?> UpdateConversationAsync(string id, string userId, string title, string? contextJson = null)
    {
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return null;
            }

            conversation.Title = title;
            conversation.ContextJson = contextJson;
            conversation.LastActiveAt = DateTime.UtcNow;

            await _unitOfWork.Conversations.UpdateAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Updated conversation {ConversationId}", id);
            return conversation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversation {ConversationId}", id);
            throw;
        }
    }

    /// <summary>
    /// Archive a conversation
    /// </summary>
    public async Task<bool> ArchiveConversationAsync(string id, string userId)
    {
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return false;
            }

            conversation.IsArchived = true;
            await _unitOfWork.Conversations.UpdateAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Archived conversation {ConversationId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving conversation {ConversationId}", id);
            throw;
        }
    }

    /// <summary>
    /// Delete a conversation and all its messages
    /// </summary>
    public async Task<bool> DeleteConversationAsync(string id, string userId)
    {
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return false;
            }

            // Delete all messages in the conversation first
            var messages = await _unitOfWork.Messages.GetByConversationIdAsync(id);
            foreach (var message in messages)
            {
                await _unitOfWork.Messages.DeleteAsync(message);
            }

            // Delete the conversation
            await _unitOfWork.Conversations.DeleteAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Deleted conversation {ConversationId} and its messages", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversation {ConversationId}", id);
            throw;
        }
    }

    /// <summary>
    /// Get conversation with messages
    /// </summary>
    public async Task<Conversation?> GetConversationWithMessagesAsync(string id, string userId)
    {
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAndUserIdAsync(id, userId);
            if (conversation == null)
            {
                return null;
            }

            // Load messages separately to avoid circular references
            var messages = await _unitOfWork.Messages.GetByConversationIdAsync(id);
            conversation.Messages = messages.ToList();

            return conversation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation {ConversationId} with messages", id);
            throw;
        }
    }

    /// <summary>
    /// Update conversation last active time
    /// </summary>
    public async Task UpdateLastActiveAsync(string id)
    {
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(id);
            if (conversation != null)
            {
                conversation.LastActiveAt = DateTime.UtcNow;
                await _unitOfWork.Conversations.UpdateAsync(conversation);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last active time for conversation {ConversationId}", id);
            throw;
        }
    }
}