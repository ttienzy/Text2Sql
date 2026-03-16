namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Unit of Work interface for managing database transactions
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Connection repository
    /// </summary>
    IConnectionRepository Connections { get; }

    /// <summary>
    /// Conversation repository
    /// </summary>
    IConversationRepository Conversations { get; }

    /// <summary>
    /// Message repository
    /// </summary>
    IMessageRepository Messages { get; }

    /// <summary>
    /// Agent job repository
    /// </summary>
    IAgentJobRepository AgentJobs { get; }

    /// <summary>
    /// Save all changes to the database
    /// </summary>
    Task<int> SaveChangesAsync();

    /// <summary>
    /// Begin a database transaction
    /// </summary>
    Task BeginTransactionAsync();

    /// <summary>
    /// Commit the current transaction
    /// </summary>
    Task CommitTransactionAsync();

    /// <summary>
    /// Rollback the current transaction
    /// </summary>
    Task RollbackTransactionAsync();
}