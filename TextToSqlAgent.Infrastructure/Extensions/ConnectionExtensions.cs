using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Extensions;

/// <summary>
/// Extension methods for the Connection entity to support multi-database operations.
/// </summary>
public static class ConnectionExtensions
{
    /// <summary>
    /// Converts the string-based Provider field to the DatabaseProvider enum.
    /// Handles common aliases (e.g., "postgres" → PostgreSql, "mssql" → SqlServer).
    /// </summary>
    public static DatabaseProvider GetDatabaseProvider(this Connection connection)
    {
        if (string.IsNullOrEmpty(connection.Provider))
            return DatabaseProvider.SqlServer;

        return connection.Provider.ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => DatabaseProvider.PostgreSql,
            "mysql" => DatabaseProvider.MySql,
            "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
            _ => DatabaseProvider.SqlServer
        };
    }

    /// <summary>
    /// Gets the database name from the Connection entity directly.
    /// Preferred over parsing connection strings — the entity already has the Database field.
    /// </summary>
    public static string GetDatabaseName(this Connection connection)
    {
        return connection.Database ?? string.Empty;
    }

    /// <summary>
    /// Gets the default port for the connection's provider type.
    /// </summary>
    public static int GetDefaultPort(this Connection connection)
    {
        return connection.GetDatabaseProvider() switch
        {
            DatabaseProvider.PostgreSql => 5432,
            DatabaseProvider.MySql => 3306,
            DatabaseProvider.SqlServer => 1433,
            _ => 1433
        };
    }
}
