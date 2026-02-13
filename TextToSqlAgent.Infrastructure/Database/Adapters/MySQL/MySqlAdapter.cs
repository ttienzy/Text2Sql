using System.Data;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.MySQL;

/// <summary>
/// MySQL database adapter - Placeholder for future implementation
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
        throw new NotImplementedException(
            "MySQL support is planned for a future release.\n" +
            "Please use SQL Server for now.\n\n" +
            "To implement MySQL support:\n" +
            "1. Install NuGet package: MySqlConnector\n" +
            "2. Implement connection creation using MySqlConnection\n" +
            "3. Implement schema scanning for MySQL\n" +
            "4. Configure MySQL-specific error handling");
    }

    public Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        throw new NotImplementedException("MySQL schema scanning not yet implemented.");
    }

    public Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        throw new NotImplementedException("MySQL connection testing not yet implemented.");
    }

    public bool IsTransientError(Exception ex)
    {
        // MySQL transient errors would be checked here
        return false;
    }

    public string GetSafeIdentifier(string identifier)
    {
        // MySQL uses backticks for identifiers
        return $"`{identifier}`";
    }

    public string GetSystemPrompt()
    {
        throw new NotImplementedException("MySQL-specific SQL generation prompt not yet implemented.");
    }

    public string GetCorrectionSystemPrompt()
    {
        throw new NotImplementedException("MySQL-specific SQL correction prompt not yet implemented.");
    }
}
