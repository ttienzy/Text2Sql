using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

/// <summary>
/// Redis-backed confirmation store for DML/DDL SSE confirm flow.
/// 
/// Key layout:
///   pending:confirm:{confirmId}         → PendingConfirmation JSON (TTL = TimeoutSeconds + 10s buffer)
///   pending:confirm:{confirmId}:result  → ConfirmationResult JSON  (TTL = 30s after decision)
///
/// Flow:
///   1. SSE controller calls StoreAsync() after generating SQL preview
///   2. SSE controller polls GetResultAsync() every 1s waiting for user decision
///   3. /confirm endpoint calls SetResultAsync() when user approves/cancels
///   4. SSE controller picks up the result and continues execution (or closes)
///   5. CleanupAsync() removes all keys after completion
/// </summary>
public class RedisConfirmationStore : IConfirmationStore
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisConfirmationStore> _logger;

    private const string KeyPrefix = "pending:confirm:";
    private const string ResultSuffix = ":result";

    // Buffer beyond the user timeout to avoid race conditions
    private const int TtlBufferSeconds = 10;
    // How long to keep the result after the decision is made
    private const int ResultTtlSeconds = 30;

    public RedisConfirmationStore(
        IConnectionMultiplexer redis,
        ILogger<RedisConfirmationStore> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<string> StoreAsync(PendingConfirmation confirmation, CancellationToken ct = default)
    {
        try
        {
            // Generate confirmId if not already set
            if (string.IsNullOrEmpty(confirmation.ConfirmId))
            {
                confirmation.ConfirmId = Guid.NewGuid().ToString("N")[..12]; // Short ID for URL friendliness
            }

            confirmation.CreatedAt = DateTime.UtcNow;

            var key = $"{KeyPrefix}{confirmation.ConfirmId}";
            var json = JsonSerializer.Serialize(confirmation);
            var ttl = TimeSpan.FromSeconds(confirmation.TimeoutSeconds + TtlBufferSeconds);

            await _db.StringSetAsync(key, json, ttl);

            _logger.LogInformation(
                "[ConfirmationStore] Stored pending confirmation {ConfirmId}: {OpType} on {Table} (TTL: {Ttl}s)",
                confirmation.ConfirmId, confirmation.OperationType, confirmation.TargetTable, ttl.TotalSeconds);

            return confirmation.ConfirmId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfirmationStore] Failed to store confirmation");
            throw;
        }
    }

    public async Task<PendingConfirmation?> GetAsync(string confirmId, CancellationToken ct = default)
    {
        try
        {
            var key = $"{KeyPrefix}{confirmId}";
            var cached = await _db.StringGetAsync(key);

            if (!cached.HasValue)
            {
                _logger.LogDebug("[ConfirmationStore] Confirmation {ConfirmId} not found (expired?)", confirmId);
                return null;
            }

            return JsonSerializer.Deserialize<PendingConfirmation>(cached.ToString()!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfirmationStore] Failed to get confirmation {ConfirmId}", confirmId);
            return null;
        }
    }

    public async Task SetResultAsync(string confirmId, ConfirmationResult result, CancellationToken ct = default)
    {
        try
        {
            result.ConfirmId = confirmId;
            result.RespondedAt = DateTime.UtcNow;

            var key = $"{KeyPrefix}{confirmId}{ResultSuffix}";
            var json = JsonSerializer.Serialize(result);

            await _db.StringSetAsync(key, json, TimeSpan.FromSeconds(ResultTtlSeconds));

            _logger.LogInformation(
                "[ConfirmationStore] User responded to {ConfirmId}: {Action}",
                confirmId, result.Action);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfirmationStore] Failed to set result for {ConfirmId}", confirmId);
            throw;
        }
    }

    public async Task<ConfirmationResult?> GetResultAsync(string confirmId, CancellationToken ct = default)
    {
        try
        {
            var key = $"{KeyPrefix}{confirmId}{ResultSuffix}";
            var cached = await _db.StringGetAsync(key);

            if (!cached.HasValue)
            {
                return null; // No decision yet
            }

            return JsonSerializer.Deserialize<ConfirmationResult>(cached.ToString()!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConfirmationStore] Failed to poll result for {ConfirmId}", confirmId);
            return null;
        }
    }

    public async Task CleanupAsync(string confirmId, CancellationToken ct = default)
    {
        try
        {
            var pendingKey = $"{KeyPrefix}{confirmId}";
            var resultKey = $"{KeyPrefix}{confirmId}{ResultSuffix}";

            await Task.WhenAll(
                _db.KeyDeleteAsync(pendingKey),
                _db.KeyDeleteAsync(resultKey)
            );

            _logger.LogDebug("[ConfirmationStore] Cleaned up keys for {ConfirmId}", confirmId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConfirmationStore] Failed to cleanup {ConfirmId}", confirmId);
            // Non-critical — keys will auto-expire via TTL
        }
    }
}
