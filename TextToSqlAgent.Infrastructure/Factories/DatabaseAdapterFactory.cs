using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database.Adapters;

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
            _ => throw new NotSupportedException($"Provider '{_config.Provider}' is not supported yet.")
        };
    }
}
