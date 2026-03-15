using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Services;

/// <summary>
/// Implementation of ITokenQuotaService for managing user token quotas
/// </summary>
public class TokenQuotaService : ITokenQuotaService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenQuotaService> _logger;

    public TokenQuotaService(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<TokenQuotaService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    private int DefaultDailyQuota => _configuration.GetValue<int>("TokenQuota:DefaultDailyLimit", 100000);

    /// <inheritdoc/>
    public async Task<TokenQuota> GetUserQuotaAsync(string userId)
    {
        var dailyLimit = await GetDailyLimitAsync(userId);
        var usedToday = await GetTodayUsageAsync(userId);
        var resetAt = GetNextResetTime();

        return new TokenQuota
        {
            DailyLimit = dailyLimit,
            UsedToday = usedToday,
            Remaining = Math.Max(0, dailyLimit - usedToday),
            ResetAt = resetAt,
            IsUnlimited = dailyLimit == int.MaxValue
        };
    }

    /// <inheritdoc/>
    public async Task<bool> HasQuotaAsync(string userId, int tokens)
    {
        var dailyLimit = await GetDailyLimitAsync(userId);

        // If unlimited quota, always return true
        if (dailyLimit == int.MaxValue)
        {
            return true;
        }

        var usedToday = await GetTodayUsageAsync(userId);
        return (usedToday + tokens) <= dailyLimit;
    }

    /// <inheritdoc/>
    public async Task ConsumeTokenAsync(string userId, int inputTokens, int outputTokens, string model)
    {
        var totalTokens = inputTokens + outputTokens;

        // Calculate cost (approximate - can be adjusted based on model pricing)
        var cost = CalculateCost(totalTokens, model);

        var tokenUsage = new TokenUsage
        {
            UserId = userId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            Model = model,
            Cost = cost,
            Timestamp = DateTime.UtcNow
        };

        _dbContext.TokenUsages.Add(tokenUsage);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Consumed {TotalTokens} tokens ({Input} input, {Output} output) for user {UserId}. Cost: ${Cost:F6}",
            totalTokens, inputTokens, outputTokens, userId, cost);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TokenUsage>> GetUsageHistoryAsync(string userId, DateTime? from = null, DateTime? to = null)
    {
        var query = _dbContext.TokenUsages.Where(tu => tu.UserId == userId);

        if (from.HasValue)
        {
            query = query.Where(tu => tu.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(tu => tu.Timestamp <= to.Value);
        }

        return await query
            .OrderByDescending(tu => tu.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task ResetQuotaAsync(string userId)
    {
        // In this implementation, quotas are automatically reset based on the date
        // This method can be used to manually reset or for admin operations
        _logger.LogInformation("Reset quota requested for user {UserId}", userId);

        // For now, we don't need to do anything since quotas are calculated dynamically
        // from the TokenUsages table based on the current date
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<int> GetTodayUsageAsync(string userId)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        return await _dbContext.TokenUsages
            .Where(tu => tu.UserId == userId && tu.Timestamp >= today && tu.Timestamp < tomorrow)
            .SumAsync(tu => tu.TotalTokens);
    }

    /// <inheritdoc/>
    public async Task<int> GetDailyLimitAsync(string userId)
    {
        // Check for user-specific quota configuration
        // This could be extended to read from a user settings table or configuration
        var customQuota = _configuration[$"TokenQuota:User:{userId}:DailyLimit"];
        if (int.TryParse(customQuota, out var userQuota))
        {
            return userQuota;
        }

        // Check for role-based quota
        var roleQuota = _configuration["TokenQuota:DefaultDailyLimit"];
        if (int.TryParse(roleQuota, out var defaultQuota))
        {
            return defaultQuota;
        }

        // Return default quota
        return DefaultDailyQuota;
    }

    private static DateTime GetNextResetTime()
    {
        // Reset at midnight UTC
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1);
    }

    private static decimal CalculateCost(int totalTokens, string model)
    {
        // Approximate pricing (can be adjusted based on actual model pricing)
        // Using GPT-4o as baseline: ~$5/1M input tokens, ~$15/1M output tokens
        var inputRate = model.ToLowerInvariant() switch
        {
            var m when m.Contains("gpt-4") || m.Contains("gpt4") => 0.000005m, // $5/1M
            var m when m.Contains("gpt-3.5") || m.Contains("gpt35") => 0.0000015m, // $1.5/1M
            var m when m.Contains("gemini") => 0.0000035m, // $3.5/1M
            _ => 0.000005m // Default to GPT-4 pricing
        };

        var outputRate = inputRate * 3; // Output is typically 3x the input rate

        // Assume 70% input, 30% output split for calculation
        var inputCost = (int)(totalTokens * 0.7) * inputRate;
        var outputCost = (int)(totalTokens * 0.3) * outputRate;

        return inputCost + outputCost;
    }
}
