using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Console.Configuration;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Factories;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

namespace TextToSqlAgent.Console.Setup;

public static class DependencyInjection
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]═══ Configuration Loading ═══[/]");
        AnsiConsole.WriteLine();

        // Step 1: Load API Key with explicit priority
        var configManager = new AppConfigurationManager(configuration);
        var (apiKey, source) = configManager.LoadOpenAIApiKey();

        // ✅ PHASE 0 FIX: Don't throw if API key missing, just warn
        if (string.IsNullOrEmpty(apiKey))
        {
            AnsiConsole.MarkupLine("[yellow]⚠ OpenAI API Key not found[/]");
            AnsiConsole.MarkupLine("[dim]  You can configure it later using /config command[/]");
            AnsiConsole.WriteLine();
            apiKey = "not-configured"; // Placeholder to prevent null errors
            source = "not configured";
        }
        else
        {
            // Step 2: Validate API Key
            if (!configManager.ValidateApiKeyFormat(apiKey, out var validationError))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ API Key Validation Warning: {validationError}[/]");
                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLine($"[green]✓ API Key loaded from:[/] [cyan]{source}[/]");
            AnsiConsole.MarkupLine($"[dim]  Key: {MaskApiKey(apiKey)}[/]");
            AnsiConsole.WriteLine();
        }

        // Step 3: Load other configurations
        var openAIConfig = LoadOpenAIConfig(configuration);
        openAIConfig.ApiKey = apiKey; // Set API key explicitly

        var databaseConfig = LoadDatabaseConfig(configuration);
        var agentConfig = LoadAgentConfig(configuration);
        var qdrantConfig = LoadQdrantConfig(configuration);
        var ragConfig = LoadRAGConfig(configuration);

        // Step 4: Final validation (warn only, don't throw)
        if (!openAIConfig.IsValid(out var configError))
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ OpenAI Configuration Warning: {configError}[/]");
            AnsiConsole.MarkupLine($"[dim]  You can configure it later using /config command[/]");
            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓ OpenAI Configuration:[/]");
            AnsiConsole.MarkupLine($"[dim]  Model: {openAIConfig.Model}[/]");
            AnsiConsole.MarkupLine($"[dim]  Embedding: {openAIConfig.EmbeddingModel}[/]");
            AnsiConsole.MarkupLine($"[dim]  Temperature: {openAIConfig.Temperature}[/]");
            AnsiConsole.WriteLine();
        }

        // Step 5: Register configurations
        services.AddSingleton(openAIConfig);
        services.AddSingleton(databaseConfig);
        services.AddSingleton(agentConfig);
        services.AddSingleton(qdrantConfig);
        services.AddSingleton(ragConfig);

        // Step 6: Register ConfigurationManager for runtime use
        services.AddSingleton(configManager);

        // Step 7: Register services
        RegisterCoreServices(services);
        RegisterInfrastructureServices(services);
        RegisterPlugins(services);
        RegisterAgent(services);
        ConfigureLogging(services);

        AnsiConsole.MarkupLine("[green]✓ All services registered successfully[/]");
        AnsiConsole.WriteLine();
    }

    private static OpenAIConfig LoadOpenAIConfig(IConfiguration configuration)
    {
        var config = new OpenAIConfig();
        configuration.GetSection("OpenAI").Bind(config);
        // Note: ApiKey will be set explicitly after loading from secure store/env
        return config;
    }

    private static DatabaseConfig LoadDatabaseConfig(IConfiguration configuration)
    {
        var config = new DatabaseConfig();
        configuration.GetSection("Database").Bind(config);
        return config;
    }

    private static AgentConfig LoadAgentConfig(IConfiguration configuration)
    {
        var config = new AgentConfig();
        configuration.GetSection("Agent").Bind(config);
        return config;
    }

    private static QdrantConfig LoadQdrantConfig(IConfiguration configuration)
    {
        var config = new QdrantConfig();
        configuration.GetSection("Qdrant").Bind(config);
        return config;
    }

    private static RAGConfig LoadRAGConfig(IConfiguration configuration)
    {
        var config = new RAGConfig();
        configuration.GetSection("RAG").Bind(config);
        return config;
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
            return "***";

        return $"{apiKey.Substring(0, 7)}...{apiKey.Substring(apiKey.Length - 4)}";
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.AddTransient<NormalizePromptTask>();
    }

    private static void RegisterInfrastructureServices(IServiceCollection services)
    {
        // Register OpenAI clients only
        services.AddSingleton<OpenAIClient>();
        services.AddSingleton<OpenAIEmbeddingClient>();

        // Register interfaces directly to OpenAI implementations
        services.AddSingleton<ILLMClient, OpenAIClient>();
        services.AddSingleton<IEmbeddingClient, OpenAIEmbeddingClient>();

        // Database adapters (SQL Server only)
        services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>();

        // Database adapter factory
        services.AddSingleton<DatabaseAdapterFactory>();

        // Register IDatabaseAdapter using factory
        services.AddSingleton<IDatabaseAdapter>(sp =>
        {
            var factory = sp.GetRequiredService<DatabaseAdapterFactory>();
            return factory.CreateAdapter();
        });

        // Database services
        services.AddSingleton<SchemaScanner>();
        services.AddSingleton<SqlExecutor>();

        // Vector Store - Fix circular dependency with named registrations
        // Register concrete implementations first
        services.AddSingleton<QdrantService>();
        services.AddSingleton<QdrantVectorStore>(); // ✅ Register QdrantVectorStore
        services.AddSingleton<InMemoryVectorStore>();

        // Register IVectorStore with FallbackVectorStore that uses concrete types
        services.AddSingleton<IVectorStore>(sp =>
        {
            var qdrant = sp.GetRequiredService<QdrantVectorStore>(); // ✅ Use QdrantVectorStore
            var inMemory = sp.GetRequiredService<InMemoryVectorStore>();
            var logger = sp.GetRequiredService<ILogger<FallbackVectorStore>>();
            return new FallbackVectorStore(qdrant, inMemory, logger);
        });

        // Memory Cache for query embedding caching
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1000; // Limit to 1000 entries
        });

        // RAG services
        services.AddSingleton<SchemaIndexer>();
        services.AddSingleton<KeywordSchemaRetriever>();
        services.AddSingleton<SchemaRetriever>();

        // Error handlers
        TextToSqlAgent.Infrastructure.ErrorHandling.ErrorHandlerServiceExtensions.AddErrorHandlers(services);
    }

    private static void RegisterPlugins(IServiceCollection services)
    {
        services.AddTransient<IntentAnalysisPlugin>();
        services.AddTransient<SqlGeneratorPlugin>();
        services.AddTransient<SqlCorrectorPlugin>();

        // NEW: Agentic AI plugins
        services.AddTransient<QueryValidatorPlugin>();
        services.AddTransient<QueryExplainerPlugin>();
    }

    private static void RegisterAgent(IServiceCollection services)
    {
        // PHASE 1: Query Routing (fast-path for greetings/out-of-scope)
        services.AddSingleton<IQueryRouter, FastPathQueryRouter>();
        services.AddSingleton<Console.Services.ConsoleRequestProcessor>();

        // LAZY LOADING: Register factory for on-demand service creation
        services.AddSingleton<IAgentServiceFactory, LazyAgentServiceFactory>();

        // PRIMARY: Enhanced Agentic AI Orchestrator (now lightweight with lazy loading!)
        services.AddSingleton<EnhancedAgentOrchestrator>();

        // LEGACY: Keep for backward compatibility (not used in Console)
        services.AddSingleton<TextToSqlAgentOrchestrator>();

        // Conversation Manager - required by CommandHandler and LazyAgentServiceFactory
        services.AddSingleton<CoreferenceResolver>();
        services.AddSingleton<ConversationManager>();

        // PHASE 1: Register Core Ports with Adapters
        RegisterCorePorts(services);

        // Production services for ReAct Agent
        services.AddSingleton<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(sp =>
            new Infrastructure.Caching.SimpleMemoryCache());
        services.AddSingleton<Infrastructure.Caching.CacheService>();
        services.AddSingleton(new Infrastructure.Caching.CacheOptions());
        services.AddSingleton<Infrastructure.Security.SqlInjectionPrevention>();
        services.AddSingleton<Infrastructure.Security.QueryCostEstimator>();
        services.AddSingleton<Infrastructure.Security.RateLimiter>();

        // ReAct Agent with full production features
        Infrastructure.Extensions.ServiceCollectionExtensions.AddReActAgentProduction(services);
    }

    private static void RegisterCorePorts(IServiceCollection services)
    {
        // Register adapters
        services.AddSingleton<Application.Adapters.CachedSchemaProvider>();
        services.AddSingleton<Application.Adapters.QueryValidatorAdapter>();
        services.AddSingleton<Application.Adapters.IntentAnalyzerAdapter>();
        services.AddSingleton<Application.Adapters.SchemaRetrieverAdapter>();
        services.AddSingleton<Application.Adapters.SqlGeneratorAdapter>();
        services.AddSingleton<Application.Adapters.SqlExecutorAdapter>();
        services.AddSingleton<Application.Adapters.SqlCorrectorAdapter>();
        services.AddSingleton<Application.Adapters.ConversationStoreAdapter>();
        services.AddSingleton<Application.Adapters.IntelligentResultFormatter>();

        // Register ports
        services.AddSingleton<Core.Ports.ISchemaProvider>(sp =>
            sp.GetRequiredService<Application.Adapters.CachedSchemaProvider>());
        services.AddSingleton<Core.Ports.IQueryValidator>(sp =>
            sp.GetRequiredService<Application.Adapters.QueryValidatorAdapter>());
        services.AddSingleton<Core.Ports.IIntentAnalyzer>(sp =>
            sp.GetRequiredService<Application.Adapters.IntentAnalyzerAdapter>());
        services.AddSingleton<Core.Ports.ISchemaRetriever>(sp =>
            sp.GetRequiredService<Application.Adapters.SchemaRetrieverAdapter>());
        services.AddSingleton<Core.Ports.ISqlGenerator>(sp =>
            sp.GetRequiredService<Application.Adapters.SqlGeneratorAdapter>());
        services.AddSingleton<Core.Ports.ISqlExecutor>(sp =>
            sp.GetRequiredService<Application.Adapters.SqlExecutorAdapter>());
        services.AddSingleton<Core.Ports.ISqlCorrector>(sp =>
            sp.GetRequiredService<Application.Adapters.SqlCorrectorAdapter>());
        services.AddSingleton<Core.Ports.IConversationStore>(sp =>
            sp.GetRequiredService<Application.Adapters.ConversationStoreAdapter>());
        services.AddSingleton<Core.Ports.IResultFormatter>(sp =>
            sp.GetRequiredService<Application.Adapters.IntelligentResultFormatter>());

        // PHASE 2: Register QueryPipeline
        services.AddSingleton<Application.Pipelines.QueryPipeline>();
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger);
        });
    }
}
