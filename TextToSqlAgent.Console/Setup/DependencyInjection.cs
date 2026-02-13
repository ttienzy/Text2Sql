using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TextToSqlAgent.Console.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Analysis;
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
        // Load configurations
        var geminiConfig = LoadGeminiConfig(configuration);
        var openAIConfig = LoadOpenAIConfig(configuration);
        var databaseConfig = LoadDatabaseConfig(configuration);
        var agentConfig = LoadAgentConfig(configuration);
        var qdrantConfig = LoadQdrantConfig(configuration);
        var ragConfig = LoadRAGConfig(configuration);

        // Validate based on selected provider
        ValidateConfiguration(configuration, geminiConfig, openAIConfig);

        // Register configurations
        services.AddSingleton(geminiConfig);
        services.AddSingleton(openAIConfig);
        services.AddSingleton(databaseConfig);
        services.AddSingleton(agentConfig);
        services.AddSingleton(qdrantConfig);
        services.AddSingleton(ragConfig);


        // Register services
        RegisterCoreServices(services);
        RegisterInfrastructureServices(services);
        RegisterPlugins(services);
        RegisterAgent(services);
        ConfigureLogging(services);
    }

    private static GeminiConfig LoadGeminiConfig(IConfiguration configuration)
    {
        var config = new GeminiConfig();
        configuration.GetSection("Gemini").Bind(config);
        return config;
    }

    private static OpenAIConfig LoadOpenAIConfig(IConfiguration configuration)
    {
        var config = new OpenAIConfig();
        configuration.GetSection("OpenAI").Bind(config);
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

    private static void ValidateConfiguration(IConfiguration configuration, GeminiConfig geminiConfig, OpenAIConfig openAIConfig)
    {
        var providerString = configuration["LLMProvider"] ?? "Gemini";
        
        if (!Enum.TryParse<LLMProvider>(providerString, ignoreCase: true, out var provider))
        {
            throw new InvalidOperationException(
                $"❌ Invalid LLMProvider '{providerString}'!\n\n" +
                "Valid values: 'Gemini' or 'OpenAI'\n" +
                "Set it in appsettings.json: \"LLMProvider\": \"Gemini\" or \"OpenAI\"");
        }

        switch (provider)
        {
            case LLMProvider.Gemini:
                if (string.IsNullOrEmpty(geminiConfig.ApiKey))
                {
                    throw new InvalidOperationException(
                        "❌ Gemini API Key not found!\n\n" +
                        "Please set it using one of these methods:\n" +
                        "1. User Secrets: dotnet user-secrets set \"Gemini:ApiKey\" \"YOUR_KEY\"\n" +
                        "2. Environment Variable: GEMINI_API_KEY=YOUR_KEY\n" +
                        "3. appsettings.Development.json (not recommended for production)");
                }
                System.Console.WriteLine($"✅ Using Gemini Provider - Model: {geminiConfig.Model}, Embedding: {geminiConfig.EmbeddingModel}");
                break;

            case LLMProvider.OpenAI:
                if (string.IsNullOrEmpty(openAIConfig.ApiKey))
                {
                    throw new InvalidOperationException(
                        "❌ OpenAI API Key not found!\n\n" +
                        "Please set it using one of these methods:\n" +
                        "1. User Secrets: dotnet user-secrets set \"OpenAI:ApiKey\" \"YOUR_KEY\"\n" +
                        "2. Environment Variable: OPENAI_API_KEY=YOUR_KEY\n" +
                        "3. appsettings.Development.json (not recommended for production)");
                }
                System.Console.WriteLine($"✅ Using OpenAI Provider - Model: {openAIConfig.Model}, Embedding: {openAIConfig.EmbeddingModel}");
                break;

            default:
                throw new InvalidOperationException($"Unsupported LLM provider: {provider}");
        }
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.AddTransient<NormalizePromptTask>();
    }

    private static void RegisterInfrastructureServices(IServiceCollection services)
    {
        // Register concrete implementations (still available for direct use if needed)
        services.AddSingleton<GeminiClient>();
        services.AddSingleton<GeminiEmbeddingClient>();
        services.AddSingleton<OpenAIClient>();
        services.AddSingleton<OpenAIEmbeddingClient>();

        // Register factories
        services.AddSingleton<LLMClientFactory>();
        services.AddSingleton<EmbeddingClientFactory>();

        // Register interfaces using factories
        services.AddSingleton<ILLMClient>(sp =>
        {
            var factory = sp.GetRequiredService<LLMClientFactory>();
            return factory.CreateClient();
        });

        services.AddSingleton<IEmbeddingClient>(sp =>
        {
            var factory = sp.GetRequiredService<EmbeddingClientFactory>();
            return factory.CreateClient();
        });

        // Database adapters
        services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>();
        services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.MySQL.MySqlAdapter>();
        services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSQL.PostgreSqlAdapter>();
        services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SQLite.SQLiteAdapter>();
        
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

        // RAG services
        services.AddSingleton<QdrantService>();
        services.AddSingleton<SchemaIndexer>();
        services.AddSingleton<SchemaRetriever>();

        // Error handlers
        TextToSqlAgent.Infrastructure.ErrorHandling.ErrorHandlerServiceExtensions.AddErrorHandlers(services);
    }


    private static void RegisterPlugins(IServiceCollection services)
    {
        services.AddTransient<IntentAnalysisPlugin>();
        services.AddTransient<SqlGeneratorPlugin>();
        services.AddTransient<SqlCorrectorPlugin>();
    }

    private static void RegisterAgent(IServiceCollection services)
    {
        services.AddSingleton<TextToSqlAgentOrchestrator>();
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