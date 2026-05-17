using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSql;

/// <summary>
/// PostgreSQL database adapter using Npgsql.
/// Implements all 6 IDatabaseAdapter responsibilities for PostgreSQL databases.
/// </summary>
public class PostgreSqlAdapter : IDatabaseAdapter
{
    private readonly ILogger<PostgreSqlAdapter> _logger;

    public PostgreSqlAdapter(ILogger<PostgreSqlAdapter> logger)
    {
        _logger = logger;
    }

    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public IDbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PostgreSqlAdapter] Connection test failed");
            return false;
        }
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

    public bool IsTransientError(Exception ex)
    {
        if (ex is NpgsqlException npgsqlEx)
        {
            // Check PostgresException (subclass) for SqlState codes
            if (npgsqlEx is PostgresException pgEx)
            {
                var transientStates = new[]
                {
                    "08006", // connection_failure
                    "08001", // sqlclient_unable_to_establish_sqlconnection
                    "08004", // sqlserver_rejected_establishment_of_sqlconnection
                    "40001", // serialization_failure
                    "40P01", // deadlock_detected
                    "53300", // too_many_connections
                    "53400", // configuration_limit_exceeded
                    "57P01", // admin_shutdown
                    "57P02", // crash_shutdown
                    "57P03", // cannot_connect_now
                };
                return transientStates.Contains(pgEx.SqlState);
            }

            // Generic NpgsqlException (e.g., broken connection, timeout)
            return npgsqlEx.IsTransient;
        }

        return false;
    }

    public string GetSafeIdentifier(string identifier)
    {
        // PostgreSQL uses double quotes for identifiers
        return $"\"{identifier}\"";
    }

    public string ApplyLimit(string sql, int limit)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // Check if LIMIT or OFFSET is already present
        if (sql.IndexOf("LIMIT ", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("OFFSET ", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sql;
        }

        // Do not add LIMIT to aggregate queries
        if (sql.IndexOf("GROUP BY", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("COUNT(", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("SUM(", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("AVG(", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sql;
        }

        // Append LIMIT at the end of the query
        var trimmed = sql.TrimEnd().TrimEnd(';');
        return $"{trimmed}\nLIMIT {limit}";
    }

    private async Task<List<TableInfo>> ScanTablesAsync(IDbConnection connection, CancellationToken ct)
    {
        var sql = @"
            SELECT 
                table_schema AS ""Schema"",
                table_name AS ""TableName""
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
              AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_name";

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
                column_name AS ""ColumnName"",
                data_type AS ""DataType"",
                CASE WHEN is_nullable = 'YES' THEN true ELSE false END AS ""IsNullable"",
                character_maximum_length AS ""MaxLength""
            FROM information_schema.columns
            WHERE table_schema = @Schema AND table_name = @Table
            ORDER BY ordinal_position";

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
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
              AND tc.table_schema = @Schema
              AND tc.table_name = @Table
            ORDER BY kcu.ordinal_position";

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
                tc.table_schema || '.' || tc.table_name AS ""FromTable"",
                kcu.column_name AS ""FromColumn"",
                ccu.table_schema || '.' || ccu.table_name AS ""ToTable"",
                ccu.column_name AS ""ToColumn""
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name
                AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')";

        var relationships = await connection.QueryAsync<RelationshipInfo>(
            new CommandDefinition(sql, cancellationToken: ct));

        return relationships.ToList();
    }
}
