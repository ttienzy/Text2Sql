using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Enriches query context with schema metadata using direct Redis/Cache lookup (O(1) performance)
/// NO Qdrant - table names are already known from AST parsing
/// </summary>
public class SchemaEnricher
{
    private readonly IDistributedCache _cache;

    public SchemaEnricher(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Enriches schema context for given table names
    /// Uses direct Redis lookup (O(1)) - NO Qdrant vector search
    /// </summary>
    public async Task<SchemaContext> EnrichSchemaAsync(
        List<string> tableNames,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var context = new SchemaContext();
        var connectionScope = ConnectionFingerprint.Compute(connectionString);

        foreach (var tableName in tableNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var normalizedTableName = tableName.Trim();
            var cacheKey = $"schema:tables:{connectionScope}:{normalizedTableName.ToLowerInvariant()}";
            TableSchema? tableSchema = null;

            try
            {
                if (!await TableExistsAsync(normalizedTableName, connectionString, cancellationToken))
                {
                    context.MissingTables.Add(normalizedTableName);
                    continue;
                }

                var cachedSchema = await _cache.GetStringAsync(cacheKey, cancellationToken);

                if (!string.IsNullOrEmpty(cachedSchema))
                {
                    // Cache hit
                    tableSchema = JsonSerializer.Deserialize<TableSchema>(cachedSchema);
                }
                else
                {
                    // Cache miss - load from database
                    tableSchema = await LoadSchemaFromDatabaseAsync(normalizedTableName, connectionString, cancellationToken);

                    if (tableSchema != null)
                    {
                        // Cache for 1 hour
                        var options = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                        };

                        await _cache.SetStringAsync(
                            cacheKey,
                            JsonSerializer.Serialize(tableSchema),
                            options,
                            cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                context.EnrichmentWarnings.Add(
                    $"Failed to load schema for table '{normalizedTableName}': {ex.Message}");
                continue;
            }

            if (tableSchema != null)
            {
                context.Tables.Add(tableSchema);
            }
            else
            {
                context.EnrichmentWarnings.Add(
                    $"Schema metadata is unavailable for table '{normalizedTableName}'.");
            }
        }

        return context;
    }

    private async Task<bool> TableExistsAsync(
        string tableName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TOP 1 1
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = @TableName
              AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    /// <summary>
    /// Loads table schema from database using INFORMATION_SCHEMA
    /// </summary>
    private async Task<TableSchema> LoadSchemaFromDatabaseAsync(
        string tableName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var schema = new TableSchema { TableName = tableName };

        // Get columns
        schema.Columns = await GetColumnsAsync(connection, tableName, cancellationToken);

        // Get indexes
        schema.Indexes = await GetIndexesAsync(connection, tableName, cancellationToken);

        // Mark indexed columns for downstream context building
        var indexedColumns = schema.Indexes
            .SelectMany(i => i.Columns)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in schema.Columns)
        {
            column.HasIndex = indexedColumns.Contains(column.ColumnName);
        }

        // Get foreign keys
        schema.ForeignKeys = await GetForeignKeysAsync(connection, tableName, cancellationToken);

        // Get row count (approximate)
        schema.RowCount = await GetRowCountAsync(connection, tableName, cancellationToken);

        return schema;
    }

    private async Task<List<ColumnInfo>> GetColumnsAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new List<ColumnInfo>();

        var sql = @"
            SELECT 
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PRIMARY_KEY,
                CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_FOREIGN_KEY
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    AND ku.TABLE_NAME = @TableName
            ) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
            LEFT JOIN (
                SELECT ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
                    AND ku.TABLE_NAME = @TableName
            ) fk ON c.COLUMN_NAME = fk.COLUMN_NAME
            WHERE c.TABLE_NAME = @TableName";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES",
                IsPrimaryKey = reader.GetInt32(3) == 1,
                IsForeignKey = reader.GetInt32(4) == 1
            });
        }

        return columns;
    }

    private async Task<List<IndexInfo>> GetIndexesAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var indexes = new List<IndexInfo>();

        var sql = @"
            SELECT 
                i.name AS IndexName,
                COL_NAME(ic.object_id, ic.column_id) AS ColumnName,
                i.is_unique AS IsUnique,
                i.type_desc AS IndexType
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE OBJECT_NAME(i.object_id) = @TableName
                AND i.name IS NOT NULL
            ORDER BY i.name, ic.key_ordinal";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var indexDict = new Dictionary<string, IndexInfo>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var indexName = reader.GetString(0);
            var columnName = reader.GetString(1);

            if (!indexDict.ContainsKey(indexName))
            {
                indexDict[indexName] = new IndexInfo
                {
                    IndexName = indexName,
                    IsUnique = reader.GetBoolean(2),
                    IsClustered = reader.GetString(3) == "CLUSTERED",
                    Columns = new List<string>()
                };
            }

            indexDict[indexName].Columns.Add(columnName);
        }

        return indexDict.Values.ToList();
    }

    private async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var foreignKeys = new List<ForeignKeyInfo>();

        var sql = @"
            SELECT 
                fk.name AS ForeignKeyName,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
                OBJECT_NAME(fkc.referenced_object_id) AS ReferencedTable,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumn
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            WHERE OBJECT_NAME(fk.parent_object_id) = @TableName";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add(new ForeignKeyInfo
            {
                ForeignKeyName = reader.GetString(0),
                ColumnName = reader.GetString(1),
                ReferencedTable = reader.GetString(2),
                ReferencedColumn = reader.GetString(3)
            });
        }

        return foreignKeys;
    }

    private async Task<long> GetRowCountAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT SUM(p.rows) AS RowCount
            FROM sys.partitions p
            JOIN sys.tables t ON p.object_id = t.object_id
            WHERE t.name = @TableName
                AND p.index_id IN (0, 1)";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != DBNull.Value ? Convert.ToInt64(result) : 0;
    }
}
