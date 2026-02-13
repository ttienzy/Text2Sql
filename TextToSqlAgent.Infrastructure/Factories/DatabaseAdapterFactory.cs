using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer;
using TextToSqlAgent.Infrastructure.Database.Adapters.MySQL;
using TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSQL;
using TextToSqlAgent.Infrastructure.Database.Adapters.SQLite;


namespace TextToSqlAgent.Infrastructure.Factories;

public class DatabaseAdapterFactory
{
    private readonly DatabaseConfig _config;
    private readonly IServiceProvider _serviceProvider;

    public DatabaseAdapterFactory(DatabaseConfig config, IServiceProvider serviceProvider)
    {
        _config = config;
        _serviceProvider = serviceProvider;
    }

    public IDatabaseAdapter CreateAdapter()
    {
        return _config.Provider switch
        {
            DatabaseProvider.SqlServer => _serviceProvider.GetRequiredService<SqlServerAdapter>(),
            DatabaseProvider.MySQL => _serviceProvider.GetRequiredService<MySqlAdapter>(),
            DatabaseProvider.PostgreSQL => _serviceProvider.GetRequiredService<PostgreSqlAdapter>(),
            DatabaseProvider.SQLite => _serviceProvider.GetRequiredService<SQLiteAdapter>(),
            _ => throw new NotSupportedException(
                $"Database provider '{_config.Provider}' is not supported yet.\n\n" +
                $"Supported providers:\n" +
                $"- SqlServer (fully implemented)\n" +
                $"- MySQL (planned)\n" +
                $"- PostgreSQL (planned)\n" +
                $"- SQLite (planned)")
        };
    }

}
