using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Services;

/// <summary>
/// Represents token quota information for a user
/// </summary>
public class TokenQuota
{
    /// <summary>
    /// Daily token limit for the user
    /// </summary>
    public int DailyLimit { get; set; }

    /// <summary>
    /// Number of tokens used today
    /// </summary>
    public int UsedToday { get; set; }

    /// <summary>
    /// Remaining tokens available today
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    /// Date and time when the quota will reset
    /// </summary>
    public DateTime ResetAt { get; set; }

    /// <summary>
    /// Whether the user has unlimited quota (admin)
    /// </summary>
    public bool IsUnlimited { get; set; }
}

/// <summary>
/// Service interface for managing user token quotas
/// </summary>
public interface ITokenQuotaService
{
    /// <summary>
    /// Gets the current token quota for a user
    /// </summary>
    /// <param name="userId">The user ID to get quota for</param>
    /// <returns>Token quota information</returns>
    Task<TokenQuota> GetUserQuotaAsync(string userId);

    /// <summary>
    /// Checks if a user has enough quota for a request
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <param name="tokens">Number of tokens needed</param>
    /// <returns>True if the user has enough quota</returns>
    Task<bool> HasQuotaAsync(string userId, int tokens);

    /// <summary>
    /// Consumes tokens from a user's quota
    /// </summary>
    /// <param name="userId">The user ID to consume tokens from</param>
    /// <param name="inputTokens">Number of input tokens consumed</param>
    /// <param name="outputTokens">Number of output tokens consumed</param>
    /// <param name="model">The AI model used</param>
    Task ConsumeTokenAsync(string userId, int inputTokens, int outputTokens, string model);

    /// <summary>
    /// Gets the token usage history for a user
    /// </summary>
    /// <param name="userId">The user ID to get history for</param>
    /// <param name="from">Optional start date</param>
    /// <param name="to">Optional end date</param>
    /// <returns>List of token usage records</returns>
    Task<IEnumerable<TokenUsage>> GetUsageHistoryAsync(string userId, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Resets the daily quota for a user
    /// </summary>
    /// <param name="userId">The user ID to reset quota for</param>
    Task ResetQuotaAsync(string userId);

    /// <summary>
    /// Gets the total tokens used by a user for today
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>Total tokens used today</returns>
    Task<int> GetTodayUsageAsync(string userId);

    /// <summary>
    /// Gets the daily limit for a user (can be configured per user)
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>Daily token limit</returns>
    Task<int> GetDailyLimitAsync(string userId);
}
