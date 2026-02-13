using System.Data;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.SQLite;

/// <summary>
/// SQLite database adapter - Placeholder for future implementation
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
        throw new NotImplementedException(
            "SQLite support is planned for a future release.\n" +
            "Please use SQL Server for now.\n\n" +
            "To implement SQLite support:\n" +
            "1. Install NuGet package: Microsoft.Data.Sqlite\n" +
            "2. Implement connection creation using SqliteConnection\n" +
            "3. Implement schema scanning using sqlite_master table\n" +
            "4. Configure SQLite-specific error handling");
    }

    public Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        throw new NotImplementedException("SQLite schema scanning not yet implemented.");
    }

    public Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        throw new NotImplementedException("SQLite connection testing not yet implemented.");
    }

    public bool IsTransientError(Exception ex)
    {
        // SQLite transient errors would be checked here (SQLITE_BUSY, SQLITE_LOCKED)
        return false;
    }

    public string GetSafeIdentifier(string identifier)
    {
        // SQLite supports both brackets and double quotes
        return $"[{identifier}]";
    }

    public string GetSystemPrompt()
    {
        throw new NotImplementedException("SQLite-specific SQL generation prompt not yet implemented.");
    }

    public string GetCorrectionSystemPrompt()
    {
        throw new NotImplementedException("SQLite-specific SQL correction prompt not yet implemented.");
    }
}
