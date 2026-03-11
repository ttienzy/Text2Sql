using Microsoft.Extensions.Caching.Distributed;
using System.Collections.Concurrent;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// Simple in-memory implementation of IDistributedCache
/// For production, use Redis with Microsoft.Extensions.Caching.StackExchangeRedis
/// </summary>
public class SimpleMemoryCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly Timer _cleanupTimer;

    public SimpleMemoryCache()
    {
        // Cleanup expired entries every minute
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public byte[]? Get(string key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired)
            {
                _cache.TryRemove(key, out _);
                return null;
            }

            return entry.Value;
        }

        return null;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return Task.FromResult(Get(key));
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var expiration = options.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(1);
        var entry = new CacheEntry
        {
            Value = value,
            ExpiresAt = DateTime.UtcNow + expiration
        };

        _cache[key] = entry;
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key)
    {
        // Not implemented for simple cache
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    private void CleanupExpiredEntries(object? state)
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private class CacheEntry
    {
        public byte[] Value { get; set; } = Array.Empty<byte>();
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
