using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace TextToSqlAgent.Infrastructure.Security;

/// <summary>
/// Rate limiter for API requests
/// </summary>
public class RateLimiter
{
    private readonly ILogger<RateLimiter> _logger;
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _entries;
    private readonly Timer _cleanupTimer;

    public RateLimiter(
        ILogger<RateLimiter> logger,
        RateLimitOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new RateLimitOptions();
        _entries = new ConcurrentDictionary<string, RateLimitEntry>();

        // Cleanup expired entries every minute
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Check if request is allowed
    /// </summary>
    public RateLimitResult CheckLimit(string identifier, int cost = 1)
    {
        var now = DateTime.UtcNow;
        var entry = _entries.GetOrAdd(identifier, _ => new RateLimitEntry());

        lock (entry.Lock)
        {
            // Remove old requests outside the window
            entry.Requests.RemoveAll(r => now - r > _options.Window);

            // Check if limit exceeded
            var currentCount = entry.Requests.Count;
            var remaining = _options.MaxRequests - currentCount;

            if (currentCount >= _options.MaxRequests)
            {
                var oldestRequest = entry.Requests.Min();
                var retryAfter = _options.Window - (now - oldestRequest);

                _logger.LogWarning(
                    "Rate limit exceeded for {Identifier}: {Count}/{Max}",
                    identifier,
                    currentCount,
                    _options.MaxRequests);

                return new RateLimitResult
                {
                    IsAllowed = false,
                    Limit = _options.MaxRequests,
                    Remaining = 0,
                    RetryAfter = retryAfter,
                    ResetAt = oldestRequest + _options.Window
                };
            }

            // Add current request
            for (int i = 0; i < cost; i++)
            {
                entry.Requests.Add(now);
            }

            return new RateLimitResult
            {
                IsAllowed = true,
                Limit = _options.MaxRequests,
                Remaining = Math.Max(0, remaining - cost),
                ResetAt = entry.Requests.Min() + _options.Window
            };
        }
    }

    /// <summary>
    /// Get current usage for identifier
    /// </summary>
    public RateLimitUsage GetUsage(string identifier)
    {
        if (!_entries.TryGetValue(identifier, out var entry))
        {
            return new RateLimitUsage
            {
                Current = 0,
                Limit = _options.MaxRequests,
                Remaining = _options.MaxRequests
            };
        }

        lock (entry.Lock)
        {
            var now = DateTime.UtcNow;
            entry.Requests.RemoveAll(r => now - r > _options.Window);

            return new RateLimitUsage
            {
                Current = entry.Requests.Count,
                Limit = _options.MaxRequests,
                Remaining = Math.Max(0, _options.MaxRequests - entry.Requests.Count),
                ResetAt = entry.Requests.Count > 0 ? entry.Requests.Min() + _options.Window : null
            };
        }
    }

    /// <summary>
    /// Reset rate limit for identifier
    /// </summary>
    public void Reset(string identifier)
    {
        _entries.TryRemove(identifier, out _);
        _logger.LogInformation("Rate limit reset for {Identifier}", identifier);
    }

    /// <summary>
    /// Cleanup expired entries
    /// </summary>
    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var expiredKeys = new List<string>();

        foreach (var kvp in _entries)
        {
            lock (kvp.Value.Lock)
            {
                kvp.Value.Requests.RemoveAll(r => now - r > _options.Window);

                if (kvp.Value.Requests.Count == 0)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
        }

        foreach (var key in expiredKeys)
        {
            _entries.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired rate limit entries", expiredKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Rate limit entry for tracking requests
/// </summary>
internal class RateLimitEntry
{
    public List<DateTime> Requests { get; } = new();
    public object Lock { get; } = new();
}

/// <summary>
/// Rate limit check result
/// </summary>
public class RateLimitResult
{
    public bool IsAllowed { get; set; }
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public TimeSpan? RetryAfter { get; set; }
    public DateTime ResetAt { get; set; }
}

/// <summary>
/// Current rate limit usage
/// </summary>
public class RateLimitUsage
{
    public int Current { get; set; }
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTime? ResetAt { get; set; }
}

/// <summary>
/// Rate limit configuration
/// </summary>
public class RateLimitOptions
{
    public int MaxRequests { get; set; } = 100;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableRateLimiting { get; set; } = true;
}
