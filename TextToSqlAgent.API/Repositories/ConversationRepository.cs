using Microsoft.EntityFrameworkCore;
using TextToSqlAgent.API.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository implementation for Conversation entity
/// </summary>
public class ConversationRepository : Repository<Conversation>, IConversationRepository
{
    public ConversationRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Conversation>> GetByUserIdAsync(string userId)
    {
        return await _dbSet
            .Include(c => c.Connection)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAt)
            .ToListAsync();
    }

    public async Task<Conversation?> GetByIdAndUserIdAsync(string id, string userId)
    {
        return await _dbSet
            .Include(c => c.Connection)
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
    }

    public async Task<IEnumerable<Conversation>> GetActiveConversationsAsync(string userId)
    {
        return await _dbSet
            .Include(c => c.Connection)
            .Where(c => c.UserId == userId && !c.IsArchived)
            .OrderByDescending(c => c.LastActiveAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Conversation>> GetConversationsWithMessageCountAsync(string userId)
    {
        return await _dbSet
            .Include(c => c.Connection)
            .Include(c => c.Messages)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAt)
            .ToListAsync();
    }

    public async Task UpdateLastActiveAsync(string conversationId)
    {
        var conversation = await GetByIdAsync(conversationId);
        if (conversation != null)
        {
            conversation.LastActiveAt = DateTime.UtcNow;
            conversation.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<IEnumerable<Conversation>> GetByConnectionIdAsync(string connectionId, string userId)
    {
        return await _dbSet
            .Include(c => c.Connection)
            .Where(c => c.ConnectionId == connectionId && c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAt)
            .ToListAsync();
    }
}