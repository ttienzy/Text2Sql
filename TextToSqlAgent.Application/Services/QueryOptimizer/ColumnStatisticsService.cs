using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Service for analyzing column statistics and data skew.
/// Provides DBA senior-level insights for query optimization.
/// </summary>
public class ColumnStatisticsService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ColumnStatisticsService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public ColumnStatisticsService(
        IDistributedCache cache,
        ILogger<ColumnStatisticsService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get column statistics including data skew analysis.
    /// Results are cached with DDL-aware invalidation.
    /// </summary>
    public async Task<ColumnStatistics?> GetColumnStatisticsAsync(
        string tableName,
        string columnName,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        // Get stats last updated timestamp for DDL-aware caching
        var statsLastUpdated = await GetStatsLastUpdatedAsync(
            tableName, columnName, connectionString, cancellationToken);

        // Cache key includes timestamp → auto-invalidates when stats update
        var cacheKey = $"colstats:{tableName}:{columnName}:{statsLastUpdated:yyyyMMddHH}";

        // Try cache first
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            _logger.LogDebug("Cache hit for {Table}.{Column}", tableName, columnName);
            return JsonSerializer.Deserialize<ColumnStatistics>(cached);
        }

        // Query statistics from database
        var stats = await QueryColumnStatisticsAsync(tableName, columnName, connectionString, cancellationToken);

        if (stats != null)
        {
            // Check if statistics are stale
            if (statsLastUpdated.HasValue)
            {
                stats.LastUpdated = statsLastUpdated;

                if (IsStatisticsStale(statsLastUpdated, stats.TotalRows, stats.ModificationCounter))
                {
                    stats.IsStale = true;
                    stats.StaleWarning = $"Statistics last updated {statsLastUpdated.Value:yyyy-MM-dd HH:mm}. " +
                                        $"Consider running UPDATE STATISTICS [{tableName}].";
                    _logger.LogWarning("Stale statistics detected for {Table}.{Column}", tableName, columnName);
                }
            }

            // Cache the result
            var json = JsonSerializer.Serialize(stats);
            await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            }, cancellationToken);
        }

        return stats;
    }

    /// <summary>
    /// Get statistics for multiple columns in a table.
    /// </summary>
    public async Task<Dictionary<string, ColumnStatistics>> GetTableStatisticsAsync(
        string tableName,
        List<string> columnNames,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ColumnStatistics>();

        foreach (var columnName in columnNames)
        {
            var stats = await GetColumnStatisticsAsync(tableName, columnName, connectionString, cancellationToken);
            if (stats != null)
            {
                result[columnName] = stats;
            }
        }

        return result;
    }

    private async Task<ColumnStatistics?> QueryColumnStatisticsAsync(
        string tableName,
        string columnName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Query for basic statistics, top values, and modification counter
            var sql = $@"
                DECLARE @TotalRows BIGINT;
                DECLARE @DistinctValues INT;
                DECLARE @ModificationCounter BIGINT;
                
                SELECT @TotalRows = SUM(rows) 
                FROM sys.partitions 
                WHERE object_id = OBJECT_ID(@TableName) AND index_id IN (0,1);
                
                -- Get modification counter from statistics
                SELECT TOP 1 @ModificationCounter = sp.modification_counter
                FROM sys.stats s
                CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
                JOIN sys.stats_columns sc ON s.object_id = sc.object_id AND s.stats_id = sc.stats_id
                JOIN sys.columns c ON sc.object_id = c.object_id AND sc.column_id = c.column_id
                WHERE OBJECT_NAME(s.object_id) = @TableName AND c.name = @ColumnName
                ORDER BY sp.last_updated DESC;
                
                SELECT @DistinctValues = COUNT(DISTINCT [{columnName}]) 
                FROM {tableName};
                
                SELECT 
                    @TotalRows AS TotalRows,
                    @DistinctValues AS DistinctValues,
                    ISNULL(@ModificationCounter, 0) AS ModificationCounter,
                    (
                        SELECT TOP 10 
                            [{columnName}] AS Value,
                            COUNT(*) AS Count,
                            CAST(COUNT(*) * 100.0 / @TotalRows AS DECIMAL(5,2)) AS Percentage
                        FROM {tableName}
                        WHERE [{columnName}] IS NOT NULL
                        GROUP BY [{columnName}]
                        ORDER BY COUNT(*) DESC
                        FOR JSON PATH
                    ) AS TopValuesJson;
            ";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ColumnName", columnName);
            command.CommandTimeout = 30;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var totalRows = reader.GetInt64(0);
                var distinctValues = reader.GetInt32(1);
                var modificationCounter = reader.GetInt64(2);
                var topValuesJson = reader.IsDBNull(3) ? "[]" : reader.GetString(3);

                var topValues = JsonSerializer.Deserialize<List<TopValue>>(topValuesJson) ?? new List<TopValue>();

                // Calculate skew factor (0-1, higher = more skewed)
                var skewFactor = 0.0;
                if (totalRows > 0 && topValues.Any())
                {
                    var maxCount = topValues.Max(v => v.Count);
                    skewFactor = (double)maxCount / totalRows;
                }

                // Calculate selectivity (0-1, lower = more selective)
                var selectivity = totalRows > 0 ? (double)distinctValues / totalRows : 0;

                return new ColumnStatistics
                {
                    TableName = tableName,
                    ColumnName = columnName,
                    TotalRows = totalRows,
                    DistinctValues = distinctValues,
                    Selectivity = selectivity,
                    SkewFactor = skewFactor,
                    TopValues = topValues,
                    SkewLevel = GetSkewLevel(skewFactor),
                    IndexRecommendation = GetIndexRecommendation(selectivity, skewFactor, totalRows),
                    ModificationCounter = modificationCounter
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query column statistics for {Table}.{Column}", tableName, columnName);
            return null;
        }
    }

    private SkewLevel GetSkewLevel(double skewFactor)
    {
        if (skewFactor >= 0.9) return SkewLevel.Extreme;
        if (skewFactor >= 0.7) return SkewLevel.High;
        if (skewFactor >= 0.5) return SkewLevel.Moderate;
        if (skewFactor >= 0.3) return SkewLevel.Low;
        return SkewLevel.None;
    }

    private string GetIndexRecommendation(double selectivity, double skewFactor, long totalRows)
    {
        // High selectivity (many distinct values) + low skew = good index candidate
        if (selectivity > 0.1 && skewFactor < 0.5 && totalRows > 10000)
        {
            return "Excellent index candidate - high selectivity, low skew";
        }

        // Low selectivity but low skew = filtered index candidate
        if (selectivity < 0.01 && skewFactor < 0.5 && totalRows > 10000)
        {
            return "Consider filtered index for minority values";
        }

        // High skew = index may not be used for majority value
        if (skewFactor > 0.7)
        {
            return "Index will only be effective for minority values due to high data skew";
        }

        // Very low selectivity = index not recommended
        if (selectivity < 0.001)
        {
            return "Index not recommended - very low selectivity";
        }

        return "Index may provide moderate benefit";
    }

    // ========== Phase 2: Statistics Freshness & DDL Invalidation ==========

    /// <summary>
    /// Get statistics last updated timestamp from SQL Server
    /// </summary>
    private async Task<DateTime?> GetStatsLastUpdatedAsync(
        string tableName,
        string columnName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = @"
                SELECT TOP 1 sp.last_updated
                FROM sys.stats s
                CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
                JOIN sys.stats_columns sc ON s.object_id = sc.object_id 
                                           AND s.stats_id = sc.stats_id
                JOIN sys.columns c ON sc.object_id = c.object_id 
                                   AND sc.column_id = c.column_id
                WHERE OBJECT_NAME(s.object_id) = @TableName
                  AND c.name = @ColumnName
                ORDER BY sp.last_updated DESC";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ColumnName", columnName);
            command.CommandTimeout = 5;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result != DBNull.Value ? (DateTime?)result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get stats last updated for {Table}.{Column}", tableName, columnName);
            return null;
        }
    }

    /// <summary>
    /// Check if statistics are stale based on age and modification counter
    /// </summary>
    private bool IsStatisticsStale(DateTime? lastUpdated, long rowCount, long modificationCounter)
    {
        if (lastUpdated == null) return true;

        // Stale if:
        // (a) > 7 days since last update, OR
        // (b) modification_counter > rowCount * 0.2 (20% rows changed)
        var ageInDays = (DateTime.UtcNow - lastUpdated.Value).TotalDays;
        return ageInDays > 7 || (rowCount > 0 && modificationCounter > rowCount * 0.2);
    }

    /// <summary>
    /// Invalidate statistics cache for a table after DDL operations.
    /// Call this from DDL pipeline after successful DDL execution.
    /// </summary>
    public async Task InvalidateTableStatisticsCacheAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Invalidating statistics cache for table: {TableName}", tableName);

        // IDistributedCache doesn't support wildcard deletion
        // For Redis: would need to use StackExchange.Redis directly
        // For now: cache will auto-invalidate on next stats update via timestamp in key

        // Note: With DDL-aware cache keys (including timestamp), 
        // cache automatically invalidates when stats are updated
        // This method is a placeholder for explicit invalidation if needed

        await Task.CompletedTask;
    }
}

/// <summary>
/// Column statistics with data skew analysis.
/// </summary>
public class ColumnStatistics
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public long TotalRows { get; set; }
    public int DistinctValues { get; set; }
    public double Selectivity { get; set; }
    public double SkewFactor { get; set; }
    public SkewLevel SkewLevel { get; set; }
    public List<TopValue> TopValues { get; set; } = new();
    public string IndexRecommendation { get; set; } = string.Empty;

    // Phase 2: Staleness tracking
    public bool IsStale { get; set; }
    public string? StaleWarning { get; set; }
    public long ModificationCounter { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Represents a top value in a column with its frequency.
/// </summary>
public class TopValue
{
    public string Value { get; set; } = string.Empty;
    public long Count { get; set; }
    public decimal Percentage { get; set; }
}

/// <summary>
/// Data skew severity level.
/// </summary>
public enum SkewLevel
{
    None,       // < 30% - evenly distributed
    Low,        // 30-50% - slight skew
    Moderate,   // 50-70% - noticeable skew
    High,       // 70-90% - significant skew
    Extreme     // > 90% - extreme skew (parameter sniffing risk)
}
