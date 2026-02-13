using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Infrastructure.Prompts;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.SQLite;

/// <summary>
/// SQLite database adapter implementation using sqlite_master and PRAGMA.
/// </summary>
public class SQLiteAdapter : IDatabaseAdapter
{
    private readonly ILogger<SQLiteAdapter> _logger;

    public SQLiteAdapter(ILogger<SQLiteAdapter> logger)
    {
        _logger = logger;
    }

    public DatabaseProvider Provider => DatabaseProvider.SQLite;

    public IDbConnection CreateConnection(string connectionString)
    {
        return new SqliteConnection(connectionString);
    }

    public async Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        // In SQLite the default schema is typically "main"
        const string defaultSchema = "main";

        var tables = await ScanTablesAsync(connection, defaultSchema, ct);
        var relationships = await ScanRelationshipsAsync(connection, tables, defaultSchema, ct);

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
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLiteAdapter] Connection test failed");
            return false;
        }
    }

    public bool IsTransientError(Exception ex)
    {
        if (ex is SqliteException sqliteEx)
        {
            // SQLITE_BUSY (5) and SQLITE_LOCKED (6) are classic transient conditions
            var transientErrorCodes = new[] { 5, 6 };
            if (transientErrorCodes.Contains(sqliteEx.SqliteErrorCode))
            {
                return true;
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

        // Support dotted identifiers (e.g., table.column)
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var safeParts = parts.Select(part =>
        {
            var trimmed = part.Trim('"', '[', ']');
            trimmed = trimmed.Replace("\"", "\"\"");
            return $"\"{trimmed}\"";
        });

        return string.Join(".", safeParts);
    }

    public string GetSystemPrompt()
    {
        return SqliteGenerationPrompt.SystemPrompt;
    }

    public string GetCorrectionSystemPrompt()
    {
        return SqliteCorrectionPrompt.SystemPrompt;
    }

    public string ApplyLimit(string sql, int limit)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // Check if LIMIT is already present
        if (sql.IndexOf("LIMIT ", StringComparison.OrdinalIgnoreCase) >= 0)
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
        string schema,
        CancellationToken ct)
    {
        const string sql = @"
SELECT 
    name AS TableName
FROM sqlite_master
WHERE type = 'table'
  AND name NOT LIKE 'sqlite_%'
ORDER BY name;";

        var tableNames = await connection.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: ct));

        var result = new List<TableInfo>();

        foreach (var tableName in tableNames)
        {
            var tableInfo = new TableInfo
            {
                Schema = schema,
                TableName = tableName
            };

            tableInfo.Columns = await ScanColumnsAsync(connection, tableName, ct);
            tableInfo.PrimaryKeys = tableInfo.Columns
                .Where(c => c.IsPrimaryKey)
                .Select(c => c.ColumnName)
                .ToList();

            result.Add(tableInfo);
        }

        return result;
    }

    private sealed class SqliteColumnInfoRaw
    {
        public long Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public long NotNull { get; set; }
        public long Pk { get; set; }
    }

    private async Task<List<ColumnInfo>> ScanColumnsAsync(
        IDbConnection connection,
        string tableName,
        CancellationToken ct)
    {
        // PRAGMA table_info does not support parameterized table names,
        // but tableName is read from sqlite_master, not user input.
        var sql = $"PRAGMA table_info({GetSafeIdentifier(tableName)});";

        var rawColumns = await connection.QueryAsync<SqliteColumnInfoRaw>(
            new CommandDefinition(sql, cancellationToken: ct));

        var columns = rawColumns
            .Select(c => new ColumnInfo
            {
                ColumnName = c.Name,
                DataType = c.Type,
                IsNullable = c.NotNull == 0,
                MaxLength = null,
                IsPrimaryKey = c.Pk != 0
            })
            .ToList();

        return columns;
    }

    private sealed class SqliteForeignKeyRaw
    {
        public long Id { get; set; }
        public long Seq { get; set; }
        public string Table { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    private async Task<List<RelationshipInfo>> ScanRelationshipsAsync(
        IDbConnection connection,
        IEnumerable<TableInfo> tables,
        string schema,
        CancellationToken ct)
    {
        var relationships = new List<RelationshipInfo>();

        foreach (var table in tables)
        {
            var sql = $"PRAGMA foreign_key_list({GetSafeIdentifier(table.TableName)});";

            var rawFks = await connection.QueryAsync<SqliteForeignKeyRaw>(
                new CommandDefinition(sql, cancellationToken: ct));

            foreach (var fk in rawFks)
            {
                relationships.Add(new RelationshipInfo
                {
                    FromTable = $"{schema}.{table.TableName}",
                    FromColumn = fk.From,
                    ToTable = $"{schema}.{fk.Table}",
                    ToColumn = fk.To
                });
            }
        }

        return relationships;
    }
}

