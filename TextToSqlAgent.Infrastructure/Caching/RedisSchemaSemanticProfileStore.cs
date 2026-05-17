using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Caching;

public class RedisSchemaSemanticProfileStore : ISchemaSemanticProfileStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisSchemaSemanticProfileStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisSchemaSemanticProfileStore(
        IDistributedCache cache,
        ILogger<RedisSchemaSemanticProfileStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<SchemaSemanticProfile?> GetAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(connectionId);

        try
        {
            var json = await _cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SchemaSemanticProfile>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SemanticProfile] Failed to read profile for {ConnectionId}", connectionId);
            return null;
        }
    }

    public async Task SetAsync(
        string connectionId,
        SchemaSemanticProfile profile,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(connectionId);
        profile.ConnectionId = connectionId;
        profile.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        await _cache.SetStringAsync(key, json, cancellationToken);

        _logger.LogInformation(
            "[SemanticProfile] Saved profile for {ConnectionId}: {TableCount} table overrides",
            connectionId,
            profile.Tables.Count);
    }

    public async Task DeleteAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(connectionId);
        await _cache.RemoveAsync(key, cancellationToken);
        _logger.LogInformation("[SemanticProfile] Deleted profile for {ConnectionId}", connectionId);
    }

    private static string GetKey(string connectionId) => $"semantic-profile:{connectionId}";
}
