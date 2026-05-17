using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;
using TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSql;


namespace TextToSqlAgent.Infrastructure.Factories;

/// <summary>
/// Factory that resolves the correct IDatabaseAdapter based on the current DatabaseConfig.Provider.
/// Provider is resolved per-request via AsyncLocal override from DatabaseConfigContext.
/// </summary>
public class DatabaseAdapterFactory
{
    private readonly DatabaseConfig _config;
    private readonly IServiceProvider _serviceProvider;

    public DatabaseAdapterFactory(DatabaseConfig config, IServiceProvider serviceProvider)
    {
        _config = config;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Creates the appropriate database adapter for the current request's provider.
    /// Reads DatabaseConfig.Provider which checks AsyncLocal override first.
    /// </summary>
    public IDatabaseAdapter CreateAdapter()
    {
        return _config.Provider switch
        {
            DatabaseProvider.SqlServer => _serviceProvider.GetRequiredService<SqlServerAdapter>(),
            DatabaseProvider.PostgreSql => _serviceProvider.GetRequiredService<PostgreSqlAdapter>(),
            _ => throw new NotSupportedException(
                $"Database provider '{_config.Provider}' is not supported. Supported providers: SqlServer, PostgreSql.")
        };
    }

    /// <summary>
    /// Creates an adapter for a specific provider (bypasses config/AsyncLocal).
    /// Useful for ConnectionService.TestConnection where provider is known from entity.
    /// </summary>
    public IDatabaseAdapter CreateAdapter(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => _serviceProvider.GetRequiredService<SqlServerAdapter>(),
            DatabaseProvider.PostgreSql => _serviceProvider.GetRequiredService<PostgreSqlAdapter>(),
            _ => throw new NotSupportedException(
                $"Database provider '{provider}' is not supported. Supported providers: SqlServer, PostgreSql.")
        };
    }
}
