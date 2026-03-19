using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Cache service for DB Explorer data using Redis
/// Caches schema, analysis, and graph data
/// </summary>
public class DbExplorerCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DbExplorerCacheService> _logger;
    private readonly TimeSpan _schemaCacheDuration = TimeSpan.FromHours(1);
    private readonly TimeSpan _analysisCacheDuration = TimeSpan.FromHours(24);
    private readonly JsonSerializerOptions _jsonOptions;

    public DbExplorerCacheService(
        IConnectionMultiplexer redis,
        ILogger<DbExplorerCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    private IDatabase GetDatabase() => _redis.GetDatabase();

    /// <summary>
    /// Get cached schema
    /// </summary>
    public EnhancedDatabaseSchema? GetCachedSchema(string connectionId)
    {
        try
        {
            var key = GetSchemaKey(connectionId);
            var db = GetDatabase();
            var value = db.StringGet(key);

            if (value.HasValue)
            {
                _logger.LogDebug("[DbExplorerCache] Schema cache HIT for {ConnectionId}", connectionId);
                return JsonSerializer.Deserialize<EnhancedDatabaseSchema>(value.ToString(), _jsonOptions);
            }

            _logger.LogDebug("[DbExplorerCache] Schema cache MISS for {ConnectionId}", connectionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error getting cached schema for {ConnectionId}", connectionId);
            return null;
        }
    }

    /// <summary>
    /// Cache schema
    /// </summary>
    public void CacheSchema(string connectionId, EnhancedDatabaseSchema schema)
    {
        try
        {
            var key = GetSchemaKey(connectionId);
            var db = GetDatabase();
            var json = JsonSerializer.Serialize(schema, _jsonOptions);

            db.StringSet(key, json, _schemaCacheDuration);
            _logger.LogInformation("[DbExplorerCache] Cached schema for {ConnectionId} (TTL: {TTL})",
                connectionId, _schemaCacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error caching schema for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Get cached analysis
    /// </summary>
    public DatabaseAnalysis? GetCachedAnalysis(string connectionId)
    {
        try
        {
            var key = GetAnalysisKey(connectionId);
            var db = GetDatabase();
            var value = db.StringGet(key);

            if (value.HasValue)
            {
                _logger.LogDebug("[DbExplorerCache] Analysis cache HIT for {ConnectionId}", connectionId);
                return JsonSerializer.Deserialize<DatabaseAnalysis>(value.ToString(), _jsonOptions);
            }

            _logger.LogDebug("[DbExplorerCache] Analysis cache MISS for {ConnectionId}", connectionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error getting cached analysis for {ConnectionId}", connectionId);
            return null;
        }
    }

    /// <summary>
    /// Cache analysis
    /// </summary>
    public void CacheAnalysis(string connectionId, DatabaseAnalysis analysis)
    {
        try
        {
            var key = GetAnalysisKey(connectionId);
            var db = GetDatabase();
            var json = JsonSerializer.Serialize(analysis, _jsonOptions);

            db.StringSet(key, json, _analysisCacheDuration);
            _logger.LogInformation("[DbExplorerCache] Cached analysis for {ConnectionId} (TTL: {TTL})",
                connectionId, _analysisCacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error caching analysis for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Get cached graph
    /// </summary>
    public GraphData? GetCachedGraph(string connectionId)
    {
        try
        {
            var key = GetGraphKey(connectionId);
            var db = GetDatabase();
            var value = db.StringGet(key);

            if (value.HasValue)
            {
                _logger.LogDebug("[DbExplorerCache] Graph cache HIT for {ConnectionId}", connectionId);
                var graph = JsonSerializer.Deserialize<GraphData>(value.ToString(), _jsonOptions);

                // Auto-migration: Detect old graph format without columns
                if (graph != null && graph.Nodes.Any() && graph.Nodes.All(n => n.Columns == null || n.Columns.Count == 0))
                {
                    _logger.LogWarning(
                        "[DbExplorerCache] Old graph format detected (missing columns) for {ConnectionId}, invalidating cache",
                        connectionId);
                    InvalidateCache(connectionId);
                    return null;
                }

                return graph;
            }

            _logger.LogDebug("[DbExplorerCache] Graph cache MISS for {ConnectionId}", connectionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error getting cached graph for {ConnectionId}", connectionId);
            return null;
        }
    }

    /// <summary>
    /// Cache graph
    /// </summary>
    public void CacheGraph(string connectionId, GraphData graph)
    {
        try
        {
            var key = GetGraphKey(connectionId);
            var db = GetDatabase();
            var json = JsonSerializer.Serialize(graph, _jsonOptions);

            db.StringSet(key, json, _analysisCacheDuration);
            _logger.LogInformation("[DbExplorerCache] Cached graph for {ConnectionId} (TTL: {TTL})",
                connectionId, _analysisCacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error caching graph for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Invalidate all cache for a connection
    /// </summary>
    public void InvalidateCache(string connectionId)
    {
        try
        {
            var db = GetDatabase();
            db.KeyDelete(GetSchemaKey(connectionId));
            db.KeyDelete(GetAnalysisKey(connectionId));
            db.KeyDelete(GetGraphKey(connectionId));
            _logger.LogInformation("[DbExplorerCache] Invalidated all cache for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DbExplorerCache] Error invalidating cache for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Check if schema fingerprint changed (for auto-invalidation)
    /// </summary>
    public bool ShouldInvalidate(string connectionId, string newFingerprint)
    {
        var schema = GetCachedSchema(connectionId);
        if (schema == null)
        {
            return false; // No cache to invalidate
        }

        var changed = schema.Fingerprint != newFingerprint;
        if (changed)
        {
            _logger.LogInformation(
                "[DbExplorerCache] Schema fingerprint changed for {ConnectionId}, invalidating cache",
                connectionId);
        }

        return changed;
    }

    private static string GetSchemaKey(string connectionId) => $"dbexplorer:schema:{connectionId}";
    private static string GetAnalysisKey(string connectionId) => $"dbexplorer:analysis:{connectionId}";
    private static string GetGraphKey(string connectionId) => $"dbexplorer:graph:{connectionId}";
}
