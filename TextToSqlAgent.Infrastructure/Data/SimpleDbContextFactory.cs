using Microsoft.EntityFrameworkCore;

namespace TextToSqlAgent.Infrastructure.Data;

/// <summary>
/// Simple factory to create DbContext instances - needed for Singleton consumers
/// </summary>
public class SimpleDbContextFactory<TContext> : IDbContextFactory<TContext> where TContext : DbContext
{
    private readonly DbContextOptions<TContext> _options;

    public SimpleDbContextFactory(DbContextOptions<TContext> options)
    {
        _options = options;
    }

    public TContext CreateDbContext()
    {
        return (TContext)Activator.CreateInstance(typeof(TContext), _options)!;
    }
}
