using System.Data;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Core.Interfaces;

public interface IDatabaseAdapter
{
    DatabaseProvider Provider { get; }

    IDbConnection CreateConnection(string connectionString);

    Task<DatabaseSchema> GetSchemaAsync(IDbConnection connection, CancellationToken ct = default);

    Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default);

    bool IsTransientError(Exception ex);
    
    string GetSafeIdentifier(string identifier);

    string GetSystemPrompt();

    string GetCorrectionSystemPrompt();
}
