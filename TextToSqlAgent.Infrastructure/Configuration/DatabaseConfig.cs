using TextToSqlAgent.Core.Enums;

namespace TextToSqlAgent.Infrastructure.Configuration;

public class DatabaseConfig
{
    private string _connectionString = string.Empty;

    /// <summary>
    /// Gets or sets the default connection string.
    /// When accessed, returns the async-local override if set (via DatabaseConfigContext),
    /// otherwise returns the default value.
    /// 
    /// This prevents race conditions when multiple requests need different connection strings.
    /// </summary>
    public string ConnectionString
    {
        get
        {
            // ✅ CRIT-2 FIX: Check async-local override first
            var asyncLocalOverride = DatabaseConfigContext.CurrentConnectionString;
            return asyncLocalOverride ?? _connectionString;
        }
        set => _connectionString = value;
    }

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
    public int CommandTimeout { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}
