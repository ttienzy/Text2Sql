using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSQL;

/// <summary>
/// PostgreSQL database adapter implementation using Npgsql and system catalogs.
/// </summary>
public class PostgreSqlAdapter : IDatabaseAdapter
{
    private readonly ILogger<PostgreSqlAdapter> _logger;

    public PostgreSqlAdapter(ILogger<PostgreSqlAdapter> logger)
    {
        _logger = logger;
    }

    public DatabaseProvider Provider => DatabaseProvider.PostgreSQL;

    public IDbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public async Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        var tables = await ScanTablesAsync(connection, ct);
        var relationships = await ScanRelationshipsAsync(connection, ct);

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

    public bool IsTransientError(Exception ex)
    {
        if (ex is PostgresException pgEx)
        {
            // PostgreSQL transient errors commonly include:
            // 08XXX: connection exceptions
            // 40001: serialization_failure
            // 40P01: deadlock_detected
            var state = pgEx.SqlState;
            if (!string.IsNullOrEmpty(state))
            {
                if (state.StartsWith("08", StringComparison.Ordinal)) // connection exception
                {
                    return true;
                }

                if (state is "40001" or "40P01")
                {
                    return true;
                }
            }
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

        // If already quoted with double quotes, assume it's safe
        if (identifier.StartsWith('"') && identifier.EndsWith('"'))
        {
            return identifier;
        }

        // Support dotted identifiers: schema.table or schema.table.column
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var safeParts = parts.Select(part =>
        {
            var trimmed = part.Trim('"');
            // Escape embedded quotes by doubling them
            trimmed = trimmed.Replace("\"", "\"\"");
            return $"\"{trimmed}\"";
        });

        return string.Join(".", safeParts);
    }

    public string GetSystemPrompt()
    {
        return PostgreSqlGenerationPrompt.SystemPrompt;
    }

    public string GetCorrectionSystemPrompt()
    {
        return PostgreSqlCorrectionPrompt.SystemPrompt;
    }

    public string ApplyLimit(string sql, int limit)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // Check if LIMIT is already present
        if (sql.IndexOf("LIMIT ", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("FETCH FIRST", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sql;
        }

        // Do not add LIMIT to aggregate queries if not present
        if (sql.IndexOf("COUNT(", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("SUM(", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sql.IndexOf("AVG(", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sql;
        }

        // Append LIMIT {limit}
        return $"{sql.TrimEnd(';')} LIMIT {limit};";
    }

    private async Task<List<TableInfo>> ScanTablesAsync(
        IDbConnection connection,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    table_schema AS ""Schema"",
    table_name   AS ""TableName""
FROM information_schema.tables
WHERE table_type = 'BASE TABLE'
  AND table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY table_schema, table_name;";

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
        const string sql = @"
SELECT
    column_name              AS ""ColumnName"",
    data_type                AS ""DataType"",
    CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END AS ""IsNullable"",
    character_maximum_length AS ""MaxLength""
FROM information_schema.columns
WHERE table_schema = @Schema AND table_name = @Table
ORDER BY ordinal_position;";

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
    a.attname AS column_name
FROM pg_index i
JOIN pg_class c ON c.oid = i.indrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
WHERE i.indisprimary
  AND n.nspname = @Schema
  AND c.relname = @Table
ORDER BY a.attnum;";

        var primaryKeys = await connection.QueryAsync<string>(
            new CommandDefinition(
                sql,
                new { Schema = schema, Table = table },
                cancellationToken: ct));

        return primaryKeys.ToList();
    }

    private async Task<List<RelationshipInfo>> ScanRelationshipsAsync(
        IDbConnection connection,
        CancellationToken ct)
    {
        const string sql = @"
SELECT
    (ns_child.nspname || '.' || child.relname)  AS ""FromTable"",
    a_child.attname                             AS ""FromColumn"",
    (ns_parent.nspname || '.' || parent.relname) AS ""ToTable"",
    a_parent.attname                            AS ""ToColumn""
FROM pg_constraint con
JOIN pg_class child ON con.conrelid = child.oid
JOIN pg_namespace ns_child ON ns_child.oid = child.relnamespace
JOIN pg_class parent ON con.confrelid = parent.oid
JOIN pg_namespace ns_parent ON ns_parent.oid = parent.relnamespace
JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS child_col(attnum, ordinality) ON TRUE
JOIN LATERAL unnest(con.confkey) WITH ORDINALITY AS parent_col(attnum, ordinality)
    ON child_col.ordinality = parent_col.ordinality
JOIN pg_attribute a_child ON a_child.attrelid = child.oid AND a_child.attnum = child_col.attnum
JOIN pg_attribute a_parent ON a_parent.attrelid = parent.oid AND a_parent.attnum = parent_col.attnum
WHERE con.contype = 'f'
  AND ns_child.nspname NOT IN ('pg_catalog', 'information_schema');";

        var relationships = await connection.QueryAsync<RelationshipInfo>(
            new CommandDefinition(sql, cancellationToken: ct));

        return relationships.ToList();
    }
}

