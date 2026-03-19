using Microsoft.EntityFrameworkCore;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository implementation for Connection entity
/// </summary>
public class ConnectionRepository : Repository<Connection>, IConnectionRepository
{
    public ConnectionRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Connection>> GetByUserIdAsync(string userId)
    {
        return await _dbSet
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Connection?> GetDefaultConnectionAsync(string userId)
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.UserId == userId && c.IsDefault && !c.IsDeleted);
    }

    public async Task<Connection?> GetByIdAndUserIdAsync(string id, string userId)
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
    }

    public async Task<IEnumerable<Connection>> GetActiveConnectionsAsync(string userId)
    {
        return await _dbSet
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task UnsetDefaultConnectionsAsync(string userId)
    {
        var defaultConnections = await _dbSet
            .Where(c => c.UserId == userId && c.IsDefault)
            .ToListAsync();

        foreach (var connection in defaultConnections)
        {
            connection.IsDefault = false;
        }
    }

    public async Task UpdateLastUsedAsync(string connectionId)
    {
        var connection = await GetByIdAsync(connectionId);
        if (connection != null)
        {
            connection.LastUsedAt = DateTime.UtcNow;
        }
    }

    public async Task UpdateSchemaSyncedAsync(string connectionId)
    {
        var connection = await GetByIdAsync(connectionId);
        if (connection != null)
        {
            connection.SchemaSyncedAt = DateTime.UtcNow;
        }
    }
}