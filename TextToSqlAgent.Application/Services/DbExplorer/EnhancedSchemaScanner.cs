using Microsoft.Extensions.Logging;
using System.Data;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Enhanced schema scanner that collects statistics
/// Extends base SchemaScanner with row counts, indexes, and column stats
/// </summary>
public class EnhancedSchemaScanner
{
    private readonly IDatabaseAdapter _adapter;
    private readonly ILogger<EnhancedSchemaScanner> _logger;

    public EnhancedSchemaScanner(
        IDatabaseAdapter adapter,
        ILogger<EnhancedSchemaScanner> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }

    /// <summary>
    /// Scan schema with enhanced statistics
    /// </summary>
    public async Task<EnhancedDatabaseSchema> ScanWithStatisticsAsync(
        string connectionString,
        bool includeStatistics = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[EnhancedSchemaScanner] Starting enhanced schema scan...");

        var result = new EnhancedDatabaseSchema();

        using var connection = _adapter.CreateConnection(connectionString);
        await OpenConnectionAsync(connection, cancellationToken);

        // Get base schema
        result.BaseSchema = await _adapter.GetSchemaAsync(connection, cancellationToken);
        result.ScannedAt = DateTime.UtcNow;

        // Generate fingerprint
        result.Fingerprint = GenerateFingerprint(result.BaseSchema);

        // Enhance with statistics
        foreach (var table in result.BaseSchema.Tables)
        {
            var enhanced = new EnhancedTableInfo
            {
                TableName = table.TableName,
                Schema = table.Schema,
                Columns = table.Columns,
                PrimaryKeys = table.PrimaryKeys,
                ColumnCount = table.Columns.Count
            };

            // Get row count
            try
            {
                enhanced.RowCount = await GetRowCountAsync(connection, table.TableName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EnhancedSchemaScanner] Failed to get row count for {Table}", table.TableName);
                enhanced.RowCount = 0;
            }

            // Get indexes
            try
            {
                enhanced.Indexes = await GetIndexesAsync(connection, table.TableName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EnhancedSchemaScanner] Failed to get indexes for {Table}", table.TableName);
            }

            // Get FK list
            enhanced.ForeignKeys = table.Columns
                .Where(c => c.IsForeignKey)
                .Select(c => c.ColumnName)
                .ToList();

            // Get column statistics (optional, can be slow)
            if (includeStatistics && enhanced.RowCount > 0 && enhanced.RowCount < 1000000)
            {
                try
                {
                    enhanced.ColumnStats = await GetColumnStatisticsAsync(
                        connection,
                        table.TableName,
                        table.Columns,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EnhancedSchemaScanner] Failed to get column stats for {Table}", table.TableName);
                }
            }

            result.EnhancedTables.Add(enhanced);
        }

        _logger.LogInformation(
            "[EnhancedSchemaScanner] ✅ Scan complete: {Tables} tables, {TotalRows} total rows",
            result.EnhancedTables.Count,
            result.EnhancedTables.Sum(t => t.RowCount));

        return result;
    }

    private async Task OpenConnectionAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is System.Data.Common.DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(cancellationToken);
        }
        else
        {
            connection.Open();
        }
    }

    private async Task<long> GetRowCountAsync(
        IDbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = $"SELECT COUNT(*) FROM [{tableName}]";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;

        var result = await ExecuteScalarAsync(command, cancellationToken);
        return Convert.ToInt64(result);
    }

    private async Task<List<IndexInfo>> GetIndexesAsync(
        IDbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var indexes = new List<IndexInfo>();

        // SQL Server specific query
        var sql = @"
            SELECT 
                i.name AS IndexName,
                COL_NAME(ic.object_id, ic.column_id) AS ColumnName,
                i.is_unique AS IsUnique,
                i.is_primary_key AS IsPrimaryKey
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            WHERE i.object_id = OBJECT_ID(@TableName)
            ORDER BY i.name, ic.key_ordinal";

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var param = command.CreateParameter();
        param.ParameterName = "@TableName";
        param.Value = tableName;
        command.Parameters.Add(param);

        using var reader = await ExecuteReaderAsync(command, cancellationToken);

        var indexDict = new Dictionary<string, IndexInfo>();

        while (await ReadAsync(reader, cancellationToken))
        {
            var indexName = reader.GetString(0);
            var columnName = reader.GetString(1);
            var isUnique = reader.GetBoolean(2);
            var isPrimaryKey = reader.GetBoolean(3);

            if (!indexDict.TryGetValue(indexName, out var index))
            {
                index = new IndexInfo
                {
                    IndexName = indexName,
                    IsUnique = isUnique,
                    IsPrimaryKey = isPrimaryKey
                };
                indexDict[indexName] = index;
            }

            index.Columns.Add(columnName);
        }

        return indexDict.Values.ToList();
    }

    private async Task<Dictionary<string, ColumnStatistics>> GetColumnStatisticsAsync(
        IDbConnection connection,
        string tableName,
        List<ColumnInfo> columns,
        CancellationToken cancellationToken)
    {
        var stats = new Dictionary<string, ColumnStatistics>();

        foreach (var column in columns.Take(20)) // Limit to first 20 columns
        {
            try
            {
                var columnStats = await GetSingleColumnStatsAsync(
                    connection,
                    tableName,
                    column,
                    cancellationToken);

                stats[column.ColumnName] = columnStats;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[EnhancedSchemaScanner] Failed to get stats for {Table}.{Column}",
                    tableName, column.ColumnName);
            }
        }

        return stats;
    }

    private async Task<ColumnStatistics> GetSingleColumnStatsAsync(
        IDbConnection connection,
        string tableName,
        ColumnInfo column,
        CancellationToken cancellationToken)
    {
        var stats = new ColumnStatistics();

        // Get null rate and distinct count
        var sql = $@"
            SELECT 
                COUNT(*) AS TotalRows,
                COUNT([{column.ColumnName}]) AS NonNullRows,
                COUNT(DISTINCT [{column.ColumnName}]) AS DistinctCount
            FROM [{tableName}]";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;

        using var reader = await ExecuteReaderAsync(command, cancellationToken);

        if (await ReadAsync(reader, cancellationToken))
        {
            var totalRows = reader.GetInt32(0);
            var nonNullRows = reader.GetInt32(1);
            var distinctCount = reader.GetInt32(2);

            stats.NullRate = totalRows > 0 ? (double)(totalRows - nonNullRows) / totalRows : 0;
            stats.DistinctCount = distinctCount;
        }

        // Get min/max/avg for numeric columns
        if (IsNumericType(column.DataType))
        {
            try
            {
                var minMaxSql = $@"
                    SELECT 
                        MIN([{column.ColumnName}]) AS MinValue,
                        MAX([{column.ColumnName}]) AS MaxValue,
                        AVG(CAST([{column.ColumnName}] AS FLOAT)) AS AvgValue
                    FROM [{tableName}]
                    WHERE [{column.ColumnName}] IS NOT NULL";

                using var minMaxCommand = connection.CreateCommand();
                minMaxCommand.CommandText = minMaxSql;
                minMaxCommand.CommandTimeout = 10;

                using var minMaxReader = await ExecuteReaderAsync(minMaxCommand, cancellationToken);

                if (await ReadAsync(minMaxReader, cancellationToken))
                {
                    if (!minMaxReader.IsDBNull(0))
                        stats.MinValue = minMaxReader.GetValue(0);
                    if (!minMaxReader.IsDBNull(1))
                        stats.MaxValue = minMaxReader.GetValue(1);
                    if (!minMaxReader.IsDBNull(2))
                        stats.AvgValue = minMaxReader.GetDouble(2);
                }
            }
            catch
            {
                // Ignore errors for min/max/avg
            }
        }

        return stats;
    }

    private bool IsNumericType(string dataType)
    {
        var numericTypes = new[] { "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "float", "real", "money" };
        return numericTypes.Any(t => dataType.ToLower().Contains(t));
    }

    private string GenerateFingerprint(DatabaseSchema schema)
    {
        var content = string.Join("|",
            schema.Tables.Select(t => $"{t.TableName}:{t.Columns.Count}"));

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }

    private async Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        if (command is System.Data.Common.DbCommand dbCommand)
        {
            return await dbCommand.ExecuteScalarAsync(cancellationToken);
        }
        return command.ExecuteScalar();
    }

    private async Task<IDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken)
    {
        if (command is System.Data.Common.DbCommand dbCommand)
        {
            return await dbCommand.ExecuteReaderAsync(cancellationToken);
        }
        return command.ExecuteReader();
    }

    private async Task<bool> ReadAsync(IDataReader reader, CancellationToken cancellationToken)
    {
        if (reader is System.Data.Common.DbDataReader dbReader)
        {
            return await dbReader.ReadAsync(cancellationToken);
        }
        return reader.Read();
    }
}
