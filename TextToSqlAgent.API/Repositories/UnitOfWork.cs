using Microsoft.EntityFrameworkCore.Storage;
using TextToSqlAgent.Infrastructure.Data;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Unit of Work implementation for managing database transactions
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;
    private bool _disposed = false;

    // Lazy initialization of repositories
    private IConnectionRepository? _connections;
    private IConversationRepository? _conversations;
    private IMessageRepository? _messages;
    private IAgentJobRepository? _agentJobs;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IConnectionRepository Connections =>
        _connections ??= new ConnectionRepository(_context);

    public IConversationRepository Conversations =>
        _conversations ??= new ConversationRepository(_context);

    public IMessageRepository Messages =>
        _messages ??= new MessageRepository(_context);

    public IAgentJobRepository AgentJobs =>
        _agentJobs ??= new AgentJobRepository(_context);

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task BeginTransactionAsync()
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("Transaction already started");
        }

        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction to commit");
        }

        try
        {
            await _context.SaveChangesAsync();
            await _transaction.CommitAsync();
        }
        catch
        {
            await _transaction.RollbackAsync();
            throw;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction to rollback");
        }

        try
        {
            await _transaction.RollbackAsync();
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _transaction?.Dispose();
            _context.Dispose();
            _disposed = true;
        }
    }
}