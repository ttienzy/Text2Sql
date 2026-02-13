using System.Data;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSQL;

/// <summary>
/// PostgreSQL database adapter - Placeholder for future implementation
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
        throw new NotImplementedException(
            "PostgreSQL support is planned for a future release.\n" +
            "Please use SQL Server for now.\n\n" +
            "To implement PostgreSQL support:\n" +
            "1. Install NuGet package: Npgsql\n" +
            "2. Implement connection creation using NpgsqlConnection\n" +
            "3. Implement schema scanning for PostgreSQL\n" +
            "4. Configure PostgreSQL-specific error handling");
    }

    public Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default)
    {
        throw new NotImplementedException("PostgreSQL schema scanning not yet implemented.");
    }

    public Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        throw new NotImplementedException("PostgreSQL connection testing not yet implemented.");
    }

    public bool IsTransientError(Exception ex)
    {
        // PostgreSQL transient errors would be checked here
        return false;
    }

    public string GetSafeIdentifier(string identifier)
    {
        // PostgreSQL uses double quotes for identifiers
        return $"\"{identifier}\"";
    }

    public string GetSystemPrompt()
    {
        throw new NotImplementedException("PostgreSQL-specific SQL generation prompt not yet implemented.");
    }

    public string GetCorrectionSystemPrompt()
    {
        throw new NotImplementedException("PostgreSQL-specific SQL correction prompt not yet implemented.");
    }
}
