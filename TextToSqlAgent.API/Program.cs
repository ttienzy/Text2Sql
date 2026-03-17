using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;
using System.Text.Json.Serialization;
using TextToSqlAgent.API.Data;
using TextToSqlAgent.API.Extensions;
using TextToSqlAgent.API.Middleware;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Infrastructure.Agent;
using TextToSqlAgent.Core.Interfaces;
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
    // 3. CONFIGURATION VALIDATION
    // ============================================
    var configuration = builder.Configuration;

    // Load and validate configuration objects
    var configService = new ConfigurationService(configuration,
        new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationService>(),
        builder.Environment);

    var validationResult = configService.ValidateConfiguration();
    if (!validationResult.IsValid)
    {
        logger.Fatal("Configuration validation failed. Cannot start application.");
        foreach (var error in validationResult.Errors)
        {
            logger.Fatal("Configuration error: {Error}", error);
        }
        throw new InvalidOperationException("Configuration validation failed. Check logs for details.");
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

    var rateLimitOptions = new RateLimitOptions
    {
        EnableRateLimiting = false, // Disabled temporarily
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

    builder.Services.AddSingleton<ILLMClient>(sp => sp.GetRequiredService<LLMClientFactory>().CreateClient());
    builder.Services.AddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<EmbeddingClientFactory>().CreateClient());

    // Infrastructure - Database (SQL Server only)
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>();
    builder.Services.AddSingleton<DatabaseAdapterFactory>();
    builder.Services.AddSingleton<IDatabaseAdapter>(sp => sp.GetRequiredService<DatabaseAdapterFactory>().CreateAdapter());
    builder.Services.AddScoped<SchemaScanner>(); // Changed to Scoped - has state
    builder.Services.AddScoped<SqlExecutor>(); // Changed to Scoped - has state

    // Infrastructure - RAG
    builder.Services.AddSingleton<QdrantService>();

    // ✅ Memory Cache for query embedding caching
    builder.Services.AddMemoryCache(options =>
    {
        options.SizeLimit = 1000; // Limit to 1000 entries
    });

    // ✅ NEW: Vector Store Abstraction with Fallback Strategy
    builder.Services.AddSingleton<QdrantVectorStore>();
    builder.Services.AddSingleton<InMemoryVectorStore>();
    builder.Services.AddSingleton<KeywordSchemaRetriever>();

    // ✅ NEW: Fallback Vector Store (Qdrant → In-Memory)
    builder.Services.AddSingleton<IVectorStore>(sp =>
    {
        var qdrantService = sp.GetRequiredService<QdrantService>();
        var qdrantLogger = sp.GetRequiredService<ILogger<QdrantVectorStore>>();
        var qdrantStore = new QdrantVectorStore(qdrantService, qdrantLogger);

        var inMemoryLogger = sp.GetRequiredService<ILogger<InMemoryVectorStore>>();
        var inMemoryStore = new InMemoryVectorStore(inMemoryLogger);

        var fallbackLogger = sp.GetRequiredService<ILogger<FallbackVectorStore>>();
        return new FallbackVectorStore(qdrantStore, inMemoryStore, fallbackLogger);
    });

    builder.Services.AddScoped<SchemaIndexer>(); // Changed to Scoped - has state
    builder.Services.AddScoped<SchemaRetriever>(); // Changed to Scoped - has state

    // ✅ NEW: Schema Auto-Sync Background Service (disabled temporarily due to connection issues)
    // builder.Services.AddHostedService<TextToSqlAgent.API.Services.SchemaSyncBackgroundService>();

    // Infrastructure - Analysis (for legacy orchestrator)
    builder.Services.AddSingleton<SqlErrorAnalyzer>();

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

    // ✅ NEW: Lazy Service Factory for Enhanced Agent (changed to Scoped)
    builder.Services.AddScoped<IAgentServiceFactory, LazyAgentServiceFactory>();

    // Legacy Orchestrator (for backward compatibility)
    builder.Services.AddScoped<TextToSqlAgentOrchestrator>(); // Changed to Scoped - has state

    // ✅ NEW: Enhanced Agentic AI Orchestrator (changed to Scoped to work with Scoped services)
    builder.Services.AddScoped<EnhancedAgentOrchestrator>();

    // ✅ NEW: Conversation-Aware Services (v2 API)
    builder.Services.AddScoped<ConversationAwareOrchestrator>();

    // ✅ NEW: Conversation Manager - required by EnhancedAgentOrchestrator
    builder.Services.AddSingleton<ConversationManager>();

    // ============================================
    // REACT AGENT SYSTEM (Phase 7)
    // ============================================

    // Production Services
    builder.Services.AddSingleton<IDistributedCache>(sp => new SimpleMemoryCache());
    builder.Services.AddScoped<CacheService>(); // Changed to Scoped - has state
    builder.Services.AddSingleton(new CacheOptions());
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

    // Token Quota Service
    builder.Services.AddScoped<TextToSqlAgent.Infrastructure.Services.ITokenQuotaService, TextToSqlAgent.Infrastructure.Services.TokenQuotaService>();

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
    builder.Services.AddScoped<IConnectionEncryptionService, ConnectionEncryptionService>();
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
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    // Database Context for Identity and API entities with secure connection string
    // For API, prioritize SQL Server connection string for production
    var connectionString = configService.GetSecureValue("ConnectionStrings:DefaultConnection") ??
                          configService.GetSecureValue("IDENTITY_CONNECTION_STRING") ??
                          "Server=.;Database=TextToSqlAgentDB;User Id=sa;Password=123;TrustServerCertificate=True;";

    logger.Information("Using database connection: {DatabaseType}",
        connectionString.Contains("Data Source=") ? "SQLite" : "SQL Server");

    // Register API DbContext
    builder.Services.AddDbContext<TextToSqlAgent.API.Data.AppDbContext>(options =>
        options.UseSqlServer(connectionString));

    // Register Infrastructure DbContext for TokenQuotaService
    builder.Services.AddDbContext<TextToSqlAgent.Infrastructure.Data.AppDbContext>(options =>
        options.UseSqlServer(connectionString));

    // ============================================
    // 4. HEALTH CHECKS
    // ============================================

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<TextToSqlAgent.API.Data.AppDbContext>("database")
        .AddCheck("api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"));

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
        .AddEntityFrameworkStores<TextToSqlAgent.API.Data.AppDbContext>()
        .AddDefaultTokenProviders();

    // JWT Authentication with secure key management
    var jwtKey = configService.GetSecureValue("Jwt:Key");
    if (string.IsNullOrEmpty(jwtKey))
    {
        throw new InvalidOperationException("JWT Key is not configured. Please set JWT_SECRET environment variable or Jwt:Key in configuration.");
    }

    // ✅ SECURITY: Configure JWT Authentication and Authorization
    builder.Services.AddJwtAuthentication(configuration);
    builder.Services.AddCustomAuthorization();

    // ✅ ADD: CORS for API access
    builder.Services.AddApiCors(configuration);

    // ✅ ADD: Response caching
    builder.Services.AddResponseCaching();

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

    app.UseHttpsRedirection();

    // Add correlation ID tracking early in pipeline
    app.UseCorrelationId();

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

    // Validate configuration on startup
    app.ValidateConfigurationOnStartup();

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
