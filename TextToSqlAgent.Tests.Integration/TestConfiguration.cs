using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Tests.Integration;

/// <summary>
/// P1-07: Test configuration helper for integration tests
/// Reads connection strings from environment variables for docker-compose compatibility
/// </summary>
public static class TestConfiguration
{
    /// <summary>
    /// Gets SQL Server connection string from environment or uses default docker-compose values
    /// Environment variable: TEST_SQL_CONNECTION_STRING
    /// </summary>
    public static string GetSqlConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");

        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }

        // Default docker-compose connection string
        return "Server=localhost,1433;Database=TextToSqlTest;User Id=sa;Password=Test@Pass123!;TrustServerCertificate=True;";
    }

    /// <summary>
    /// Gets Qdrant connection URL from environment or uses default docker-compose values
    /// Environment variable: TEST_QDRANT_URL
    /// </summary>
    public static string GetQdrantUrl()
    {
        var url = Environment.GetEnvironmentVariable("TEST_QDRANT_URL");
        return !string.IsNullOrEmpty(url) ? url : "http://localhost:6333";
    }

    /// <summary>
    /// Gets Redis connection string from environment or uses default docker-compose values
    /// Environment variable: TEST_REDIS_CONNECTION_STRING
    /// </summary>
    public static string GetRedisConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION_STRING");
        return !string.IsNullOrEmpty(connectionString) ? connectionString : "localhost:6379";
    }

    /// <summary>
    /// Creates a DatabaseConfig for integration tests
    /// </summary>
    public static DatabaseConfig CreateDatabaseConfig()
    {
        return new DatabaseConfig
        {
            ConnectionString = GetSqlConnectionString(),
            CommandTimeout = 30,
            MaxRetryAttempts = 3
        };
    }

    /// <summary>
    /// Checks if running in CI environment
    /// </summary>
    public static bool IsCI()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }
}
