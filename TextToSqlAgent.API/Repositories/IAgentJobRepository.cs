using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository interface for AgentJob operations
/// </summary>
public interface IAgentJobRepository : IRepository<AgentJob>
{
    /// <summary>
    /// Get jobs by user ID with pagination
    /// </summary>
    Task<IEnumerable<AgentJob>> GetByUserIdAsync(string userId, int skip = 0, int take = 50);

    /// <summary>
    /// Get jobs by status
    /// </summary>
    Task<IEnumerable<AgentJob>> GetByStatusAsync(JobStatus status);

    /// <summary>
    /// Get job by Hangfire job ID
    /// </summary>
    Task<AgentJob?> GetByHangfireJobIdAsync(string hangfireJobId);

    /// <summary>
    /// Get recent jobs for a user
    /// </summary>
    Task<IEnumerable<AgentJob>> GetRecentJobsAsync(string userId, int count = 10);

    /// <summary>
    /// Get job statistics for a user
    /// </summary>
    Task<(int Total, int Completed, int Failed, int Running)> GetJobStatsAsync(string userId);
}