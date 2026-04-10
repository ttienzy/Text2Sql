using TextToSqlAgent.Core.Enums;

namespace TextToSqlAgent.Infrastructure.Prompts;

/// <summary>
/// Factory for database-specific SQL generation prompts
/// Supports SQL Server, MySQL, PostgreSQL with syntax-specific templates
/// </summary>
public class DatabasePromptFactory
{
    /// <summary>
    /// Get system prompt for specific database provider
    /// </summary>
    public string GetSystemPrompt(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => SqlServerPrompts.SystemPrompt,
            DatabaseProvider.MySql => MySqlPrompts.SystemPrompt,
            DatabaseProvider.PostgreSql => PostgreSqlPrompts.SystemPrompt,
            _ => SqlServerPrompts.SystemPrompt // Default to SQL Server
        };
    }

    /// <summary>
    /// Get syntax guide for specific database provider
    /// </summary>
    public string GetSyntaxGuide(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => SqlServerPrompts.SyntaxGuide,
            DatabaseProvider.MySql => MySqlPrompts.SyntaxGuide,
            DatabaseProvider.PostgreSql => PostgreSqlPrompts.SyntaxGuide,
            _ => SqlServerPrompts.SyntaxGuide
        };
    }

    /// <summary>
    /// Get example queries for specific database provider
    /// </summary>
    public string GetExampleQueries(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => SqlServerPrompts.ExampleQueries,
            DatabaseProvider.MySql => MySqlPrompts.ExampleQueries,
            DatabaseProvider.PostgreSql => PostgreSqlPrompts.ExampleQueries,
            _ => SqlServerPrompts.ExampleQueries
        };
    }

    /// <summary>
    /// Detect database provider from connection string
    /// </summary>
    public DatabaseProvider DetectProvider(string connectionString)
    {
        var lowerConn = connectionString.ToLowerInvariant();

        if (lowerConn.Contains("mysql") || lowerConn.Contains("mariadb"))
            return DatabaseProvider.MySql;

        if (lowerConn.Contains("postgres") || lowerConn.Contains("npgsql"))
            return DatabaseProvider.PostgreSql;

        // Default to SQL Server
        return DatabaseProvider.SqlServer;
    }
}
