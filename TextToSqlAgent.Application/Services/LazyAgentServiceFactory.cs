using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Application.Services;

/// <summary>
/// Factory for lazy loading agent services on-demand
/// Improves startup time by deferring service creation until first use
/// </summary>
public interface IAgentServiceFactory
{
    QueryValidatorPlugin GetQueryValidator();
    IntentAnalysisPlugin GetIntentAnalyzer();
    SchemaScanner GetSchemaScanner();
    SchemaIndexer GetSchemaIndexer();
    SchemaRetriever GetSchemaRetriever();
    QdrantService GetQdrantService();
    SqlGeneratorPlugin GetSqlGenerator();
    SqlCorrectorPlugin GetSqlCorrector();
    QueryExplainerPlugin GetQueryExplainer();
    SqlExecutor GetSqlExecutor();
    ConversationManager GetConversationManager();
    TextToSqlAgent.Application.Services.Visualization.IPythonVisualizer GetPythonVisualizer();

    // Generic method for other services
    T GetOrCreate<T>() where T : class;
}

/// <summary>
/// Lazy implementation that creates services on first access and caches them
/// </summary>
public class LazyAgentServiceFactory : IAgentServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, object> _cache = new();
    private readonly ILogger<LazyAgentServiceFactory> _logger;

    public LazyAgentServiceFactory(
        IServiceProvider serviceProvider,
        ILogger<LazyAgentServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public T GetOrCreate<T>() where T : class
    {
        return (T)_cache.GetOrAdd(typeof(T), _ =>
        {
            _logger.LogDebug("[LazyFactory] Creating {Service}...", typeof(T).Name);
            var startTime = DateTime.UtcNow;

            var service = _serviceProvider.GetRequiredService<T>();

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogDebug("[LazyFactory] Created {Service} in {Ms}ms",
                typeof(T).Name, elapsed.TotalMilliseconds);

            return service;
        });
    }

    public QueryValidatorPlugin GetQueryValidator() => GetOrCreate<QueryValidatorPlugin>();
    public IntentAnalysisPlugin GetIntentAnalyzer() => GetOrCreate<IntentAnalysisPlugin>();
    public SchemaScanner GetSchemaScanner() => GetOrCreate<SchemaScanner>();
    public SchemaIndexer GetSchemaIndexer() => GetOrCreate<SchemaIndexer>();
    public SchemaRetriever GetSchemaRetriever() => GetOrCreate<SchemaRetriever>();
    public QdrantService GetQdrantService() => GetOrCreate<QdrantService>();
    public SqlGeneratorPlugin GetSqlGenerator() => GetOrCreate<SqlGeneratorPlugin>();
    public SqlCorrectorPlugin GetSqlCorrector() => GetOrCreate<SqlCorrectorPlugin>();
    public QueryExplainerPlugin GetQueryExplainer() => GetOrCreate<QueryExplainerPlugin>();
    public SqlExecutor GetSqlExecutor() => GetOrCreate<SqlExecutor>();
    public ConversationManager GetConversationManager() => GetOrCreate<ConversationManager>();
    public TextToSqlAgent.Application.Services.Visualization.IPythonVisualizer GetPythonVisualizer() => GetOrCreate<TextToSqlAgent.Application.Services.Visualization.IPythonVisualizer>();
}
