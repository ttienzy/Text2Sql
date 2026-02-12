using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Infrastructure.Database;

public class SchemaScanner
{
    private readonly DatabaseConfig _config;
    private readonly ILogger<SchemaScanner> _logger;

    public SchemaScanner(DatabaseConfig config, ILogger<SchemaScanner> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<DatabaseSchema> ScanAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Database Plugin] Đang quét schema database...");

        if (string.IsNullOrEmpty(_config.ConnectionString))
        {
            throw new InvalidOperationException("Connection string is not configured");
        }

        try
        {
            using var connection = new SqlConnection(_config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var schema = new DatabaseSchema
            {
                Tables = await ScanTablesAsync(connection, cancellationToken),
                Relationships = await ScanRelationshipsAsync(connection, cancellationToken),
                ScannedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "[Database Plugin] Quét hoàn tất: {TableCount} bảng, {RelationshipCount} quan hệ",
                schema.Tables.Count,
                schema.Relationships.Count);

            return schema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Database Plugin] Lỗi khi quét schema");
            throw;
        }
    }

    private async Task<List<TableInfo>> ScanTablesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT 
                TABLE_SCHEMA AS [Schema],
                TABLE_NAME AS TableName
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";

        var tables = await connection.QueryAsync<(string Schema, string TableName)>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        var result = new List<TableInfo>();

        foreach (var (schema, tableName) in tables)
        {
            var tableInfo = new TableInfo
            {
                Schema = schema,
                TableName = tableName,
                Columns = await ScanColumnsAsync(connection, schema, tableName, cancellationToken),
                PrimaryKeys = await GetPrimaryKeysAsync(connection, schema, tableName, cancellationToken)
            };

            // Mark primary key columns
            foreach (var pk in tableInfo.PrimaryKeys)
            {
                var column = tableInfo.Columns.FirstOrDefault(c => c.ColumnName == pk);
                if (column != null)
                {
                    column.IsPrimaryKey = true;
                }
            }

            result.Add(tableInfo);

            _logger.LogDebug("[Database Plugin] Scanned table: {Schema}.{Table} ({ColumnCount} columns)",
                schema, tableName, tableInfo.Columns.Count);
        }

        return result;
    }

    private async Task<List<ColumnInfo>> ScanColumnsAsync(
        SqlConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT 
                COLUMN_NAME AS ColumnName,
                DATA_TYPE AS DataType,
                CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                CHARACTER_MAXIMUM_LENGTH AS MaxLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
            ORDER BY ORDINAL_POSITION";

        var columns = await connection.QueryAsync<ColumnInfo>(
            new CommandDefinition(
                sql,
                new { Schema = schema, Table = table },
                cancellationToken: cancellationToken));

        return columns.ToList();
    }

    private async Task<List<string>> GetPrimaryKeysAsync(
        SqlConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1
            AND TABLE_SCHEMA = @Schema 
            AND TABLE_NAME = @Table
            ORDER BY ORDINAL_POSITION";

        var pks = await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new { Schema = schema, Table = table },
                cancellationToken: cancellationToken));

        return pks.ToList();
    }

    private async Task<List<RelationshipInfo>> ScanRelationshipsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT 
                OBJECT_SCHEMA_NAME(fk.parent_object_id) + '.' + OBJECT_NAME(fk.parent_object_id) AS FromTable,
                COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS FromColumn,
                OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id) AS ToTable,
                COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ToColumn
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc 
                ON fk.object_id = fkc.constraint_object_id";

        var relationships = await connection.QueryAsync<RelationshipInfo>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return relationships.ToList();
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            _logger.LogInformation("[Database Plugin] Kết nối database thành công");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Database Plugin] Không thể kết nối database");
            return false;
        }
    }
}