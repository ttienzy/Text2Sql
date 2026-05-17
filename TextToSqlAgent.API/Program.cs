using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using StackExchange.Redis;
using System.Text.Json.Serialization;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Middleware;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Application.Services.DbExplorer;
using TextToSqlAgent.Application.Extensions;
using TextToSqlAgent.Application.Adapters;
using TextToSqlAgent.Infrastructure.Agent;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Ports;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Analysis;
using TextToSqlAgent.Infrastructure.Caching;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.Infrastructure.Extensions;
using TextToSqlAgent.Infrastructure.Factories;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Infrastructure.Observability;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.Security;
using TextToSqlAgent.Infrastructure.Verification;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;
using TextToSqlAgent.Application.DependencyInjection;
using DotNetEnv;

// Load environment variables from .env file first
try
{
    var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (File.Exists(envPath))
    {
        Env.Load(envPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not load .env file: {ex.Message}");
}

var builder = WebApplication.CreateBuilder(args);

// ============================================
// 1. STRUCTURED LOGGING CONFIGURATION
// ============================================
builder.Host.ConfigureStructuredLogging();

try
{
    var logger = Log.ForContext<Program>();
    logger.Information("Starting TextToSqlAgent API...");

    // ============================================
    // 2. CONFIGURATION SERVICES
    // ============================================
    builder.Services.AddConfigurationServices(builder.Configuration);

    // ============================================
    // 3. CONFIGURATION VALIDATION (Temporarily disabled for local development)
    // ============================================
    var configuration = builder.Configuration;

    // Load and validate configuration objects
    var configService = new ConfigurationService(configuration,
        new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationService>(),
        builder.Environment);

    // ✅ CRIT-1: Configuration validation re-enabled (soft-fail in dev, hard-fail in prod)
    var validationResult = configService.ValidateConfiguration();
    if (!validationResult.IsValid)
    {
        if (builder.Environment.IsDevelopment())
        {
            logger.Warning("⚠️  Configuration validation failed in Development mode:");
            foreach (var error in validationResult.Errors)
            {
                logger.Warning("  ❌ {Error}", error);
            }
            logger.Warning("⚠️  Application will start but may crash at runtime. Fix configuration before production.");
        }
        else
        {
            logger.Fatal("Configuration validation failed. Cannot start in Production.");
            foreach (var error in validationResult.Errors)
            {
                logger.Fatal("  ❌ {Error}", error);
            }
            throw new InvalidOperationException("Configuration validation failed. Check logs for details.");
        }
    }
    else
    {
        logger.Information("✅ Configuration validation passed");
    }

    // Log warnings regardless of environment
    foreach (var warning in validationResult.Warnings)
    {
        logger.Warning("  ⚠️  {Warning}", warning);
    }

    // Load Config Objects with environment variable support
    var geminiConfig = new GeminiConfig();
    configuration.GetSection("Gemini").Bind(geminiConfig);
    // Override with environment variables
    geminiConfig.ApiKey = configService.GetSecureValue("Gemini:ApiKey") ?? geminiConfig.ApiKey;

    var openAIConfig = new OpenAIConfig();
    configuration.GetSection("OpenAI").Bind(openAIConfig);
    // Override with environment variables
    openAIConfig.ApiKey = configService.GetSecureValue("OpenAI:ApiKey") ?? openAIConfig.ApiKey;

    var databaseConfig = new DatabaseConfig();
    configuration.GetSection("Database").Bind(databaseConfig);
    // Override with environment variables
    databaseConfig.ConnectionString = configService.GetSecureValue("Database:ConnectionString") ?? databaseConfig.ConnectionString;

    var agentConfig = new AgentConfig();
    configuration.GetSection("Agent").Bind(agentConfig);

    var qdrantConfig = new QdrantConfig();
    configuration.GetSection("Qdrant").Bind(qdrantConfig);
    // Override with environment variables
    qdrantConfig.ApiKey = configService.GetSecureValue("Qdrant:ApiKey") ?? qdrantConfig.ApiKey;
    var qdrantUrl = configService.GetConfigurationValue("Qdrant:Url");
    if (!string.IsNullOrEmpty(qdrantUrl))
    {
        // Parse URL to extract host and port
        if (Uri.TryCreate(qdrantUrl, UriKind.Absolute, out var uri))
        {
            qdrantConfig.Host = uri.Host;
            qdrantConfig.Port = uri.Port != -1 ? uri.Port : 6333;
        }
    }

    var ragConfig = new RAGConfig();
    configuration.GetSection("RAG").Bind(ragConfig);

    var conversationConfig = new ConversationConfig();
    configuration.GetSection("Conversation").Bind(conversationConfig);

    // ✅ CRIT-3: Rate limiting driven by config (no longer hardcoded to false)
    var rateLimitOptions = new RateLimitOptions
    {
        EnableRateLimiting = configuration.GetValue<bool>("Production:EnableRateLimiting"),
        MaxRequests = configuration.GetValue<int?>("Production:RateLimitMaxRequests") ?? 100,
        Window = TimeSpan.FromMinutes(configuration.GetValue<int?>("Production:RateLimitWindowMinutes") ?? 1)
    };

    // Validate LLM Provider
    var providerString = configuration["LLMProvider"] ?? "OpenAI";
    if (!Enum.TryParse<LLMProvider>(providerString, ignoreCase: true, out var provider))
    {
        Log.Error("Invalid LLMProvider: {Provider}", providerString);
        throw new InvalidOperationException($"Invalid LLMProvider '{providerString}'. Valid values: 'Gemini' or 'OpenAI'");
    }

    logger.Information("Using LLM Provider: {Provider}", provider);
    logger.Information("Environment: {Environment}", builder.Environment.EnvironmentName);

    // Register Configs
    builder.Services.AddSingleton(geminiConfig);
    builder.Services.AddSingleton(openAIConfig);
    builder.Services.AddSingleton(databaseConfig);
    builder.Services.AddSingleton(agentConfig);
    builder.Services.AddSingleton(qdrantConfig);
    builder.Services.AddSingleton(ragConfig);
    builder.Services.AddSingleton(conversationConfig);

    // ============================================
    // 2. REGISTER SERVICES
    // ============================================

    // Core
    builder.Services.AddTransient<NormalizePromptTask>();

    // Infrastructure - LLM
    builder.Services.AddSingleton<GeminiClient>();
    builder.Services.AddSingleton<GeminiEmbeddingClient>();
    builder.Services.AddSingleton<OpenAIClient>();
    builder.Services.AddSingleton<OpenAIEmbeddingClient>();
    builder.Services.AddSingleton<LLMClientFactory>();
    builder.Services.AddSingleton<EmbeddingClientFactory>();

    // ✅ PHASE-2 TASK 2.2: Register caches BEFORE using them
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Caching.IntentCache>();
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Caching.QueryResultCache>();
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Caching.LLMResponseCache>();
    logger.Information("✅ Intent, Query result, and LLM response caching registered");

    builder.Services.AddSingleton<ILLMClient>(sp =>
    {
        // Create base LLM client
        var baseClient = sp.GetRequiredService<LLMClientFactory>().CreateClient();

        // ✅ PHASE-2 TASK 2.2e: Wrap with caching decorator
        var cache = sp.GetRequiredService<TextToSqlAgent.Infrastructure.Caching.LLMResponseCache>();
        var logger = sp.GetRequiredService<ILogger<TextToSqlAgent.Infrastructure.LLM.CachedLLMClient>>();

        return new TextToSqlAgent.Infrastructure.LLM.CachedLLMClient(
            baseClient, cache, logger, "default");
    });
    builder.Services.AddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<EmbeddingClientFactory>().CreateClient());

    // Infrastructure - Database (Multi-provider: SQL Server + PostgreSQL)
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>();
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSql.PostgreSqlAdapter>();
    builder.Services.AddScoped<DatabaseAdapterFactory>(); // Scoped — reads per-request provider from AsyncLocal
    builder.Services.AddScoped<IDatabaseAdapter>(sp => sp.GetRequiredService<DatabaseAdapterFactory>().CreateAdapter());
    builder.Services.AddScoped<SchemaScanner>(); // Scoped - has state
    builder.Services.AddScoped<SqlExecutor>(); // Scoped - has state

    // Infrastructure - RAG
    // ✅ TD-10: Register named Qdrant HttpClient to prevent socket exhaustion
    builder.Services.AddHttpClient("Qdrant", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddSingleton<QdrantService>();

    // ✅ Memory Cache for query embedding caching
    builder.Services.AddMemoryCache(options =>
    {
        options.SizeLimit = 1000; // Limit to 1000 entries
    });

    // ✅ Redis for DB Explorer caching
    var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ??
                         configuration["Redis:Connection"] ??
                         "127.0.0.1:6379";
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var config = ConfigurationOptions.Parse(redisConnection);
        config.AbortOnConnectFail = false; // Don't crash if Redis is unavailable
        return ConnectionMultiplexer.Connect(config);
    });
    logger.Information("Redis configured: {RedisConnection}", redisConnection);

    // ✅ NEW: Vector Store Abstraction with Fallback Strategy
    builder.Services.AddSingleton<QdrantVectorStore>();
    builder.Services.AddSingleton<InMemoryVectorStore>();
    builder.Services.AddSingleton<KeywordSchemaRetriever>();

    // ✅ NEW: Fallback Vector Store (Qdrant → In-Memory) wrapped with LRU Cache
    builder.Services.AddSingleton<IVectorStore>(sp =>
    {
        var qdrantService = sp.GetRequiredService<QdrantService>();
        var qdrantLogger = sp.GetRequiredService<ILogger<QdrantVectorStore>>();
        var qdrantStore = new QdrantVectorStore(qdrantService, qdrantLogger);

        var inMemoryLogger = sp.GetRequiredService<ILogger<InMemoryVectorStore>>();
        var inMemoryStore = new InMemoryVectorStore(inMemoryLogger);

        var fallbackLogger = sp.GetRequiredService<ILogger<FallbackVectorStore>>();
        var fallbackStore = new FallbackVectorStore(qdrantStore, inMemoryStore, fallbackLogger);

        var cache = sp.GetRequiredService<IMemoryCache>();
        var cacheLogger = sp.GetRequiredService<ILogger<CachedVectorStoreDecorator>>();

        return new CachedVectorStoreDecorator(fallbackStore, cache, cacheLogger);
    });

    builder.Services.AddScoped<SchemaIndexer>(); // Changed to Scoped - has state
    builder.Services.AddScoped<SchemaRetriever>(); // Changed to Scoped - has state
    builder.Services.AddScoped<EnhancedSchemaContextBuilder>(); // Phase 2 - Enhanced schema context

    // ⚠️ SMALL-6: Schema Auto-Sync Background Service (DISABLED due to design limitation)
    // Issue: SchemaScanner requires a specific connection ID, but background service doesn't know which connection to scan
    // Solution: Schema sync is triggered per-connection when users interact with the system
    // TODO: Refactor to support per-connection schema sync or use webhook-based detection
    // builder.Services.AddHostedService<TextToSqlAgent.API.Services.SchemaSyncBackgroundService>();

    // ⚠️ TEMP DISABLED: Schema Pre-warming Background Service
    // Temporarily disabled to avoid errors with invalid/old connections in database
    // TODO: Re-enable after cleaning up invalid connections or improving error handling
    // builder.Services.AddHostedService<TextToSqlAgent.API.Services.SchemaPrewarmingService>();

    // ✅ NEW: Approval Timeout Background Worker (checks every 1 minute)
    builder.Services.AddHostedService<TextToSqlAgent.API.BackgroundServices.ApprovalTimeoutWorker>();

    // Infrastructure - Analysis (for legacy orchestrator)
    builder.Services.AddSingleton<SqlErrorAnalyzer>();

    // ✅ IMP-4: Custom OpenTelemetry metrics (counters, histograms, gauges)
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Telemetry.AppMetrics>();

    // ✅ PHASE-2: Connection Encryption Service (Data Protection API)
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "keys")));
    builder.Services.AddSingleton<TextToSqlAgent.API.Services.IConnectionEncryptionService, TextToSqlAgent.API.Services.ConnectionEncryptionService>();
    logger.Information("✅ Connection encryption service registered with Data Protection API");

    // ✅ PHASE-2 TASK 2.2: Caching services already registered above (line 196-199)

    // Error Handlers
    TextToSqlAgent.Infrastructure.ErrorHandling.ErrorHandlerServiceExtensions.AddErrorHandlers(builder.Services);

    // Plugins (Legacy - for backward compatibility)
    builder.Services.AddTransient<IntentAnalysisPlugin>();
    builder.Services.AddTransient<SqlGeneratorPlugin>();
    builder.Services.AddTransient<SqlCorrectorPlugin>();
    builder.Services.AddTransient<QueryValidatorPlugin>();
    builder.Services.AddTransient<QueryExplainerPlugin>();
    builder.Services.AddTransient<IntelligentResponsePlugin>();

    // ✅ NEW: Query Routing
    builder.Services.AddSingleton<TextToSqlAgent.Application.Routing.IQueryRouter, TextToSqlAgent.Application.Routing.QueryRouter>();
    builder.Services.AddTransient<TextToSqlAgent.Core.Interfaces.IIntentRoutingPromptService, TextToSqlAgent.Application.Routing.PromptRegistryIntentRoutingPromptService>();

    // ✅ NEW: Lazy Service Factory for Enhanced Agent (changed to Scoped)
    builder.Services.AddScoped<IAgentServiceFactory, LazyAgentServiceFactory>();

    // Legacy Orchestrator (for backward compatibility)
    builder.Services.AddScoped<TextToSqlAgentOrchestrator>(); // Changed to Scoped - has state

    // ✅ NEW: Enhanced Agentic AI Orchestrator (changed to Scoped to work with Scoped services)
    builder.Services.AddScoped<EnhancedAgentOrchestrator>();
    builder.Services.AddScoped<ConversationTurnOrchestrator>();

    // ✅ NEW: Modular Pipeline Architecture (Phase 1 Refactor)
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.PipelineOrchestrator>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.IntentClassificationStage>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.ValidationStage>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.AgentReasoningStage>(); // Phase 4 Injection
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.SchemaRetrievalStage>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.SqlGenerationStage>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.SqlExecutionStage>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Pipeline.IPipelineStage, TextToSqlAgent.Application.Pipeline.Stages.ResponseFormattingStage>();
    logger.Information("✅ Modular pipeline registered (6 stages)");

    // ✅ NEW: Agentic Architecture (Phase 4 — ReAct Loop)
    builder.Services.AddScoped<TextToSqlAgent.Application.Agent.IAgentTool, TextToSqlAgent.Application.Agent.Tools.SchemaLookupTool>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Agent.IAgentTool, TextToSqlAgent.Application.Agent.Tools.SqlGenerationTool>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Agent.IAgentTool, TextToSqlAgent.Application.Agent.Tools.SqlExecutionTool>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Agent.IAgentTool, TextToSqlAgent.Application.Agent.Tools.QueryDecompositionTool>();
    builder.Services.AddScoped<TextToSqlAgent.Application.Agent.AgentLoop>();
    logger.Information("✅ Agentic architecture registered (4 tools + AgentLoop)");

    // ✅ NEW: Query Result Cache for pagination (lazy loading)
    builder.Services.AddSingleton<IQueryResultCache, RedisQueryResultCache>();

    // ✅ NEW: Conversation-Aware Services (v2 API)
    builder.Services.AddScoped<ConversationAwareOrchestrator>();

    // ✅ NEW: Conversation Manager - required by EnhancedAgentOrchestrator
    builder.Services.AddSingleton<CoreferenceResolver>();
    builder.Services.AddSingleton<ConversationManager>();

    // ✅ NEW: Confirmation flow store for DML/DDL safety gates
    builder.Services.AddSingleton<TextToSqlAgent.Core.Interfaces.IConfirmationStore, TextToSqlAgent.Infrastructure.Caching.RedisConfirmationStore>();

    // ============================================
    // 🎯 PHASE 1: INTENT-BASED MULTI-PIPELINE ARCHITECTURE
    // ============================================

    // Register dependencies for pipelines
    builder.Services.AddScoped<ISchemaCache, SchemaCache>();
    builder.Services.AddScoped<ISqlExecutor, SqlExecutorAdapter>();

    // Register Phase 2: Python Data Visualizer
    builder.Services.AddScoped<TextToSqlAgent.Application.Services.Visualization.IPythonVisualizer, TextToSqlAgent.Application.Services.Visualization.PythonVisualizer>();

    // Register intent-based pipelines
    builder.Services.AddIntentBasedPipelines(builder.Configuration);
    logger.Information("✅ Intent-based pipelines registered (WRITE/DDL/FORBIDDEN)");

    // ✅ SERIOUS-8 FIX: Override PipelineResponseBuilder registration with isDevelopment flag
    builder.Services.AddSingleton<PipelineResponseBuilder>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<PipelineResponseBuilder>>();
        return new PipelineResponseBuilder(logger, builder.Environment.IsDevelopment());
    });

    // ============================================
    // DB EXPLORER SERVICES
    // ============================================
    // Core infrastructure services
    builder.Services.AddSingleton<RuleEngine>();

    // Analysis and scanning services
    builder.Services.AddScoped<EnhancedSchemaScanner>();
    builder.Services.AddScoped<DatabaseAnalyzer>();
    builder.Services.AddScoped<ImplicitRelationshipDetector>();
    builder.Services.AddScoped<SemanticTagGenerator>();
    builder.Services.AddScoped<DbExplorerQdrantIndexer>();
    builder.Services.AddScoped<GraphDataBuilder>();
    builder.Services.AddScoped<QuerySuggestionService>();
    builder.Services.AddScoped<SchemaChangeDetector>();
    builder.Services.AddSingleton<DbExplorerCacheService>();

    // ============================================
    // QUERY OPTIMIZER SERVICES (Sprint 1)
    // ============================================
    builder.Services.AddQueryOptimizer();
    logger.Information("✅ Query Optimizer services registered (ScriptDom AST-based)");

    // ============================================
    // REACT AGENT SYSTEM (Phase 7)
    // ============================================

    // ✅ Production Services - Use Redis for IDistributedCache
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "TextToSqlAgent:";
    });

    builder.Services.AddScoped<CacheService>(); // Changed to Scoped - has state
    builder.Services.AddScoped<QueryPlanCache>(); // ✅ SMALL-1: Query plan cache (1hr TTL, SHA256 key)

    // ✅ INFRA-1: Bind CacheOptions from appsettings.json instead of hardcoded defaults
    // ✅ INFRA-3: CacheService now gets IConnectionMultiplexer for RemoveByPatternAsync
    // Note: IConnectionMultiplexer registered above is injected automatically as optional param
    var cacheOptions = new CacheOptions();
    configuration.GetSection("Cache").Bind(cacheOptions);
    cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(configuration.GetValue<int?>("Cache:DefaultTTLMinutes") ?? 120);
    cacheOptions.SchemaExpiration = TimeSpan.FromMinutes(configuration.GetValue<int?>("Cache:SchemaCacheTTLMinutes") ?? 2880);
    cacheOptions.EmbeddingExpiration = TimeSpan.FromMinutes(configuration.GetValue<int?>("Cache:EmbeddingCacheTTLMinutes") ?? 1440);
    cacheOptions.SqlResultExpiration = TimeSpan.FromMinutes(configuration.GetValue<int?>("Cache:QueryResultCacheTTLMinutes") ?? 60);
    cacheOptions.EnableCaching = configuration.GetValue<bool?>("Cache:EnableIntelligentCaching") ?? true;
    builder.Services.AddSingleton(cacheOptions);
    builder.Services.AddSingleton(rateLimitOptions);
    builder.Services.AddSingleton<SqlInjectionPrevention>();
    builder.Services.AddSingleton<QueryCostEstimator>();
    builder.Services.AddScoped<RateLimiter>(); // Changed to Scoped - has state

    // Register ReAct Agent with full production features
    // This includes: Base Agent, Tools, RAG, Advanced Features, Observability
    builder.Services.AddReActAgentProduction();

    // ✅ NEW: Conversation-Aware ReAct Agent (v2)
    builder.Services.AddScoped<ConversationAwareReActAgent>();

    // ============================================
    // 3. API SERVICES
    // ============================================

    // Authentication Services
    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
    builder.Services.AddScoped<IEmailService, EmailService>();

    // Token Quota Service
    builder.Services.AddScoped<TextToSqlAgent.Infrastructure.Services.ITokenQuotaService, TextToSqlAgent.Infrastructure.Services.TokenQuotaService>();

    // Approval Queue Service (Async Write/DDL Approval UX)
    builder.Services.AddScoped<IApprovalQueueService, ApprovalQueueService>();

    // Repository Pattern Services
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IConnectionRepository, ConnectionRepository>();
    builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
    builder.Services.AddScoped<IMessageRepository, MessageRepository>();
    builder.Services.AddScoped<IAgentJobRepository, AgentJobRepository>();

    // Database Services
    builder.Services.AddScoped<DatabaseInitializer>();
    builder.Services.AddScoped<DatabaseSeeder>();

    // Connection Management Services
    // Note: IConnectionEncryptionService already registered as Singleton above (line 294)
    builder.Services.AddSingleton<ConnectionIndexingTracker>();
    builder.Services.AddScoped<IConnectionService, ConnectionService>();

    // Conversation Management Services
    builder.Services.AddScoped<IConversationService, ConversationService>();

    // Query Processing Services
    builder.Services.AddScoped<IQueryProcessingService, QueryProcessingService>();

    // Vector Search Services
    builder.Services.AddScoped<IVectorSearchService, VectorSearchService>();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // ✅ Unified Response: camelCase naming for consistency with JavaScript
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;

            // ✅ Enum serialization as strings
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

            // ✅ Handle circular references
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

            // ✅ Ignore null values to reduce payload size
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

            // ✅ Pretty print for development (can be disabled in production)
            options.JsonSerializerOptions.WriteIndented = builder.Environment.IsDevelopment();
        });

    // Database Context for Identity and API entities with secure connection string
    // For API, prioritize SQL Server connection string for production
    var connectionString = configService.GetSecureValue("ConnectionStrings:DefaultConnection") ??
                          configService.GetSecureValue("IDENTITY_CONNECTION_STRING");

    if (string.IsNullOrEmpty(connectionString))
    {
        logger.Fatal("❌ Database connection string is not configured. Set IDENTITY_CONNECTION_STRING or ConnectionStrings:DefaultConnection");
        throw new InvalidOperationException("Database connection string is required. Please configure IDENTITY_CONNECTION_STRING environment variable or ConnectionStrings:DefaultConnection in appsettings.json");
    }

    logger.Information("Using database connection: {DatabaseType}",
        connectionString.Contains("Data Source=") ? "SQLite" : "SQL Server");

    builder.Services.AddDbContext<TextToSqlAgent.Infrastructure.Data.AppDbContext>(options =>
        options.UseSqlServer(connectionString));



    // ============================================
    // 4. HEALTH CHECKS
    // ============================================

    // ✅ INFRA-4: Added Redis + Qdrant health checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<TextToSqlAgent.Infrastructure.Data.AppDbContext>("database")
        .AddCheck("api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
        .AddRedis(redisConnection, name: "redis", timeout: TimeSpan.FromSeconds(3))
        .AddCheck("qdrant", () =>
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var qdrantHealthUrl = $"http://{qdrantConfig.Host}:6333/healthz";
                var response = httpClient.GetAsync(qdrantHealthUrl).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode
                    ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Qdrant is reachable")
                    : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded($"Qdrant returned {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Qdrant unreachable", ex);
            }
        });

    // Identity using Infrastructure ApplicationUser
    builder.Services.AddIdentity<TextToSqlAgent.Infrastructure.Entities.ApplicationUser, IdentityRole>(options =>
    {
        // ✅ SECURITY: Configure password requirements
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;

        // ✅ SECURITY: Configure user settings
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;
    })
        .AddEntityFrameworkStores<TextToSqlAgent.Infrastructure.Data.AppDbContext>()
        .AddDefaultTokenProviders();

    // ✅ SECURITY: Configure JWT Authentication and Authorization
    builder.Services.AddJwtAuthentication(configuration);
    builder.Services.AddCustomAuthorization();

    // ✅ ADD: CORS for API access
    builder.Services.AddApiCors(configuration);

    // ✅ ADD: Response caching
    builder.Services.AddResponseCaching();

    // ✅ IMP-3: Response compression (Gzip) — reduces payload 60-80%
    builder.Services.AddResponseCompression(opts =>
    {
        opts.EnableForHttps = true;
        opts.Providers.Add<GzipCompressionProvider>();
    });

    // ============================================
    // 4. BUILD AND CONFIGURE
    // ============================================
    var app = builder.Build();

    // Configure Pipeline with proper middleware order
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        // ✅ ADD: Global exception handler middleware for production
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                var response = new
                {
                    error = "An unexpected error occurred. Please try again later.",
                    correlationId = correlationId,
                    timestamp = DateTime.UtcNow
                };

                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
                logger.LogError("Unhandled exception in pipeline for correlation ID {CorrelationId}", correlationId);
            });
        });
    }

    // CORS must be FIRST to handle preflight requests properly
    if (app.Environment.IsDevelopment())
    {
        app.UseCors("Development");
    }
    else
    {
        app.UseCors();
    }

    // ✅ IMP-3: Response compression must be early in pipeline
    app.UseResponseCompression();

    app.UseHttpsRedirection();

    // Add correlation ID tracking early in pipeline
    app.UseCorrelationId();

    // ✅ IMP-6: Structured request logging (after correlation ID so it can log corrId)
    app.UseRequestLogging();

    // ✅ IMP-2: Header-based API versioning (X-API-Version header, defaults to v1)
    app.UseApiVersioning();

    // Add validation middleware
    app.UseValidationMiddleware();

    // P1-05: Security headers
    app.UseSecurityHeaders();

    // P1-05: Rate limiting (enabled by default in production)
    if (rateLimitOptions.EnableRateLimiting)
    {
        app.UseRateLimiting();
        logger.Information("✅ Rate limiting enabled: {MaxRequests} requests per {Window}",
            rateLimitOptions.MaxRequests,
            rateLimitOptions.Window);
    }
    else
    {
        logger.Warning("⚠️  Rate limiting is DISABLED by configuration - not recommended for production!");
    }

    // ✅ SECURITY: JWT Authentication middleware (custom middleware for additional validation)
    app.UseJwtAuthentication();

    // Health check endpoints (before authentication)
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready");

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Initialize Database with proper error handling and seeding
    using (var scope = app.Services.CreateScope())
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        try
        {
            await dbInitializer.InitializeAsync();
            logger.Information("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Failed to initialize database");
            throw; // Re-throw to prevent startup with broken database
        }
    }

    // ✅ Configuration validation is now handled at startup (lines 77-109)
    logger.Information("✅ All startup validation complete");

    logger.Information("TextToSqlAgent API started successfully");
    logger.Information("Environment: {Environment}", app.Environment.EnvironmentName);
    logger.Information("API available at: http://localhost:5000");
    logger.Information("Health check: http://localhost:5000/api/agent/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // Log configuration validation errors specifically
    if (ex is InvalidOperationException && ex.Message.Contains("Configuration validation failed"))
    {
        Log.Fatal("Configuration validation failed. Please check your environment variables and configuration files.");
        Log.Fatal("Common issues:");
        Log.Fatal("- Missing JWT_SECRET environment variable");
        Log.Fatal("- Missing OPENAI_API_KEY or GEMINI_API_KEY environment variable");
        Log.Fatal("- Invalid configuration values");
        Log.Fatal("See .env.example for required configuration");
    }

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
