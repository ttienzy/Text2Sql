using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Database.Adapters;

public class SqlServerAdapter : IDatabaseAdapter
{
    private readonly ILogger<SqlServerAdapter> _logger;

    public SqlServerAdapter(ILogger<SqlServerAdapter> logger)
    {
        _logger = logger;
    }

    public DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public IDbConnection CreateConnection(string connectionString)
    {
        return new SqlConnection(connectionString);
    }

    public async Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        var schema = new DatabaseSchema
        {
            Tables = await ScanTablesAsync(connection, ct),
            Relationships = await ScanRelationshipsAsync(connection, ct),
            ScannedAt = DateTime.UtcNow
        };
        return schema;
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SqlServerAdapter] Connection test failed");
            return false;
        }
    }

    public bool IsTransientError(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            var transientErrors = new[]
            {
                -1,    // Timeout
                -2,    // Connection broken
                1205,  // Deadlock
                4060,  // Cannot open database
                40197, // Service error
                40501, // Service busy
                40613, // Database unavailable
                49918, // Cannot process request
                49919, // Too many create/update operations
                49920  // Too many operations
            };
            return transientErrors.Contains(sqlEx.Number);
        }
        return false;
    }

    public string GetSafeIdentifier(string identifier)
    {
        return $"[{identifier}]";
    }

    public string GetSystemPrompt()
    {
        return TextToSqlAgent.Infrastructure.Prompts.SqlGenerationPrompt.SystemPrompt;
    }

    public string GetCorrectionSystemPrompt()
    {
        return TextToSqlAgent.Infrastructure.Prompts.SqlCorrectionPrompt.SystemPrompt;
    }

    private async Task<List<TableInfo>> ScanTablesAsync(IDbConnection connection, CancellationToken ct)
    {
        var sql = @"
            SELECT 
                TABLE_SCHEMA AS [Schema],
                TABLE_NAME AS TableName
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME";

        var tables = await connection.QueryAsync<(string Schema, string TableName)>(
            new CommandDefinition(sql, cancellationToken: ct));

        var result = new List<TableInfo>();

        foreach (var (schema, tableName) in tables)
        {
            var tableInfo = new TableInfo
            {
                Schema = schema,
                TableName = tableName,
                Columns = await ScanColumnsAsync(connection, schema, tableName, ct),
                PrimaryKeys = await GetPrimaryKeysAsync(connection, schema, tableName, ct)
            };

            foreach (var pk in tableInfo.PrimaryKeys)
            {
                var column = tableInfo.Columns.FirstOrDefault(c => c.ColumnName == pk);
                if (column != null)
                {
                    column.IsPrimaryKey = true;
                }
            }

            result.Add(tableInfo);
        }

        return result;
    }

    private async Task<List<ColumnInfo>> ScanColumnsAsync(
        IDbConnection connection,
        string schema,
        string table,
        CancellationToken ct)
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
                cancellationToken: ct));

        return columns.ToList();
    }

    private async Task<List<string>> GetPrimaryKeysAsync(
        IDbConnection connection,
        string schema,
        string table,
        CancellationToken ct)
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
                cancellationToken: ct));

        return pks.ToList();
    }

    private async Task<List<RelationshipInfo>> ScanRelationshipsAsync(IDbConnection connection, CancellationToken ct)
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
            new CommandDefinition(sql, cancellationToken: ct));

        return relationships.ToList();
    }
}
