using Microsoft.EntityFrameworkCore;
using TextToSqlAgent.API.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository implementation for Message entity
/// </summary>
public class MessageRepository : Repository<Message>, IMessageRepository
{
    public MessageRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Message>> GetByConversationIdAsync(string conversationId, int skip = 0, int take = 50)
    {
        return await _dbSet
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    // Overload for getting all messages (used in ConversationService)
    public async Task<IEnumerable<Message>> GetByConversationIdAsync(string conversationId)
    {
        return await _dbSet
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> GetMessageCountAsync(string conversationId)
    {
        return await _dbSet
            .CountAsync(m => m.ConversationId == conversationId);
    }

    public async Task<IEnumerable<Message>> GetRecentMessagesAsync(string conversationId, int count = 10)
    {
        return await _dbSet
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetByUserIdAsync(string userId, int page = 1, int pageSize = 50)
    {
        return await _dbSet
            .Include(m => m.Conversation)
            .Where(m => m.Conversation.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetMessagesWithSqlAsync(string conversationId)
    {
        return await _dbSet
            .Where(m => m.ConversationId == conversationId && !string.IsNullOrEmpty(m.SqlQuery))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Message>> GetRecentQueriesAsync(string userId, int limit = 5)
    {
        return await _dbSet
            .Include(m => m.Conversation)
            .Where(m => m.Conversation.UserId == userId && !string.IsNullOrEmpty(m.SqlQuery))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}