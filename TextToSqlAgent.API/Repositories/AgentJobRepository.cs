using Microsoft.EntityFrameworkCore;
using TextToSqlAgent.API.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Repositories;

/// <summary>
/// Repository implementation for AgentJob operations
/// </summary>
public class AgentJobRepository : Repository<AgentJob>, IAgentJobRepository
{
    public AgentJobRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// Get jobs by user ID with pagination
    /// </summary>
    public async Task<IEnumerable<AgentJob>> GetByUserIdAsync(string userId, int skip = 0, int take = 50)
    {
        return await _context.Set<AgentJob>()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Get jobs by status
    /// </summary>
    public async Task<IEnumerable<AgentJob>> GetByStatusAsync(JobStatus status)
    {
        return await _context.Set<AgentJob>()
            .Where(j => j.Status == status)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get job by Hangfire job ID
    /// </summary>
    public async Task<AgentJob?> GetByHangfireJobIdAsync(string hangfireJobId)
    {
        return await _context.Set<AgentJob>()
            .FirstOrDefaultAsync(j => j.HangfireJobId == hangfireJobId);
    }

    /// <summary>
    /// Get recent jobs for a user
    /// </summary>
    public async Task<IEnumerable<AgentJob>> GetRecentJobsAsync(string userId, int count = 10)
    {
        return await _context.Set<AgentJob>()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Get job statistics for a user
    /// </summary>
    public async Task<(int Total, int Completed, int Failed, int Running)> GetJobStatsAsync(string userId)
    {
        var jobs = await _context.Set<AgentJob>()
            .Where(j => j.UserId == userId)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = jobs.Sum(j => j.Count);
        var completed = jobs.FirstOrDefault(j => j.Status == JobStatus.Completed)?.Count ?? 0;
        var failed = jobs.FirstOrDefault(j => j.Status == JobStatus.Failed)?.Count ?? 0;
        var running = jobs.FirstOrDefault(j => j.Status == JobStatus.Running)?.Count ?? 0;

        return (total, completed, failed, running);
    }
}