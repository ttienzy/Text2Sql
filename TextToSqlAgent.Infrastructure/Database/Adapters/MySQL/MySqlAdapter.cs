using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.MySQL;

/// <summary>
/// MySQL database adapter implementation using MySqlConnector.
/// </summary>
public class MySqlAdapter : IDatabaseAdapter
{
    private readonly ILogger<MySqlAdapter> _logger;

    public MySqlAdapter(ILogger<MySqlAdapter> logger)
    {
        _logger = logger;
    }

    public DatabaseProvider Provider => DatabaseProvider.MySQL;

    public IDbConnection CreateConnection(string connectionString)
    {
        return new MySqlConnection(connectionString);
    }

    public async Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        var databaseName = connection.Database;

        var tables = await ScanTablesAsync(connection, databaseName, ct);
        var relationships = await ScanRelationshipsAsync(connection, databaseName, ct);

        return new DatabaseSchema
        {
            Tables = tables,
            Relationships = relationships,
            ScannedAt = DateTime.UtcNow
        };
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MySqlAdapter] Connection test failed");
            return false;
        }
    }

    public bool IsTransientError(Exception ex)
    {
        if (ex is MySqlException mysqlEx)
        {
            // Common transient MySQL errors (connection issues, deadlocks, timeouts)
            var transientErrors = new[]
            {
                1042, // Unable to connect to any of the specified MySQL hosts
                1043, // Bad handshake
                1047, // Unknown command
                1152, // Aborted connection
                1153, // Aborted connection
                1158, // Network error, read
                1159, // Network error, read
                1160, // Network error, write
                1161, // Network error, write
                1205, // Lock wait timeout exceeded
                1213, // Deadlock found
                2002, // Can't connect to MySQL server
                2006, // MySQL server has gone away
                2013  // Lost connection to MySQL server during query
            };

            return transientErrors.Contains(mysqlEx.Number);
        }

        if (ex is TimeoutException)
        {
            return true;
        }

        return ex.InnerException != null && IsTransientError(ex.InnerException);
    }

    public string GetSafeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return identifier;
        }

        // If already quoted with backticks, assume it's safe
        if (identifier.StartsWith('`') && identifier.EndsWith('`'))
        {
            return identifier;
        }

        // Support dotted identifiers (schema.table or database.schema.table)
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var safeParts = parts.Select(part =>
        {
            var trimmed = part.Trim('`');
            // Escape backticks by doubling them
            trimmed = trimmed.Replace("`", "``");
            return $"`{trimmed}`";
        });

        return string.Join(".", safeParts);
    }

    public string GetSystemPrompt()
    {
        return MySqlGenerationPrompt.SystemPrompt;
    }

    public string GetCorrectionSystemPrompt()
    {
        return MySqlCorrectionPrompt.SystemPrompt;
    }

    private async Task<List<TableInfo>> ScanTablesAsync(
        IDbConnection connection,
        string databaseName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT 
    TABLE_SCHEMA AS `Schema`,
    TABLE_NAME   AS TableName
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
  AND TABLE_SCHEMA = @Database
ORDER BY TABLE_SCHEMA, TABLE_NAME;";

        var tables = await connection.QueryAsync<(string Schema, string TableName)>(
            new CommandDefinition(sql, new { Database = databaseName }, cancellationToken: ct));

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
        const string sql = @"
SELECT 
    COLUMN_NAME               AS ColumnName,
    DATA_TYPE                 AS DataType,
    CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
    CHARACTER_MAXIMUM_LENGTH  AS MaxLength
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
ORDER BY ORDINAL_POSITION;";

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
        const string sql = @"
SELECT
    k.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
    ON t.CONSTRAINT_NAME = k.CONSTRAINT_NAME
   AND t.TABLE_SCHEMA = k.TABLE_SCHEMA
   AND t.TABLE_NAME = k.TABLE_NAME
WHERE t.CONSTRAINT_TYPE = 'PRIMARY KEY'
  AND t.TABLE_SCHEMA = @Schema
  AND t.TABLE_NAME = @Table
ORDER BY k.ORDINAL_POSITION;";

        var primaryKeys = await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new { Schema = schema, Table = table },
                cancellationToken: ct));

        return primaryKeys.ToList();
    }

    private async Task<List<RelationshipInfo>> ScanRelationshipsAsync(
        IDbConnection connection,
        string databaseName,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    CONCAT(k.TABLE_SCHEMA, '.', k.TABLE_NAME)                        AS FromTable,
    k.COLUMN_NAME                                                    AS FromColumn,
    CONCAT(k.REFERENCED_TABLE_SCHEMA, '.', k.REFERENCED_TABLE_NAME) AS ToTable,
    k.REFERENCED_COLUMN_NAME                                         AS ToColumn
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
WHERE k.TABLE_SCHEMA = @Database
  AND k.REFERENCED_TABLE_NAME IS NOT NULL;";

        var relationships = await connection.QueryAsync<RelationshipInfo>(
            new CommandDefinition(
                sql,
                new { Database = databaseName },
                cancellationToken: ct));

        return relationships.ToList();
    }
}

