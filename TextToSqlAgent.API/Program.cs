using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json.Serialization;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;
using TextToSqlAgent.Core.Tasks;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Database;
using TextToSqlAgent.Infrastructure.Factories;
using TextToSqlAgent.Infrastructure.LLM;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.VectorDB;
using TextToSqlAgent.Plugins;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting TextToSqlAgent API...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // ============================================
    // 1. CONFIGURATION VALIDATION
    // ============================================
    var configuration = builder.Configuration;
    
    // Validate required configuration
    var jwtKey = configuration["Jwt:Key"];
    if (string.IsNullOrEmpty(jwtKey) || jwtKey == "SUPER_SECRET_KEY_FOR_DEV_ONLY_12345678")
    {
        Log.Warning("⚠️  JWT Key is using default value. This is not recommended for production!");
    }

    // Load Config Objects with validation
    var geminiConfig = new GeminiConfig();
    configuration.GetSection("Gemini").Bind(geminiConfig);

    var openAIConfig = new OpenAIConfig();
    configuration.GetSection("OpenAI").Bind(openAIConfig);

    var databaseConfig = new DatabaseConfig();
    configuration.GetSection("Database").Bind(databaseConfig);

    var agentConfig = new AgentConfig();
    configuration.GetSection("Agent").Bind(agentConfig);

    var qdrantConfig = new QdrantConfig();
    configuration.GetSection("Qdrant").Bind(qdrantConfig);

    var ragConfig = new RAGConfig();
    configuration.GetSection("RAG").Bind(ragConfig);

    // Validate LLM Provider
    var providerString = configuration["LLMProvider"] ?? "OpenAI";
    if (!Enum.TryParse<LLMProvider>(providerString, ignoreCase: true, out var provider))
    {
        Log.Error("Invalid LLMProvider: {Provider}", providerString);
        throw new InvalidOperationException($"Invalid LLMProvider '{providerString}'. Valid values: 'Gemini' or 'OpenAI'");
    }

    // Validate API key based on provider
    if (provider == LLMProvider.Gemini && string.IsNullOrEmpty(geminiConfig.ApiKey))
    {
        Log.Warning("⚠️  Gemini API key is not configured. Gemini provider will fail!");
    }
    else if (provider == LLMProvider.OpenAI && string.IsNullOrEmpty(openAIConfig.ApiKey))
    {
        Log.Warning("⚠️  OpenAI API key is not configured. OpenAI provider will fail!");
    }

    Log.Information("Using LLM Provider: {Provider}", provider);

    // Register Configs
    builder.Services.AddSingleton(geminiConfig);
    builder.Services.AddSingleton(openAIConfig);
    builder.Services.AddSingleton(databaseConfig);
    builder.Services.AddSingleton(agentConfig);
    builder.Services.AddSingleton(qdrantConfig);
    builder.Services.AddSingleton(ragConfig);

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

    // Infrastructure - Database
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SqlServer.SqlServerAdapter>();
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.MySQL.MySqlAdapter>();
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.PostgreSQL.PostgreSqlAdapter>();
    builder.Services.AddSingleton<TextToSqlAgent.Infrastructure.Database.Adapters.SQLite.SQLiteAdapter>();
    builder.Services.AddSingleton<DatabaseAdapterFactory>();

    builder.Services.AddSingleton<IDatabaseAdapter>(sp => sp.GetRequiredService<DatabaseAdapterFactory>().CreateAdapter());
    builder.Services.AddSingleton<SchemaScanner>();
    builder.Services.AddSingleton<SqlExecutor>();

    // Infrastructure - RAG
    builder.Services.AddSingleton<QdrantService>();
    builder.Services.AddSingleton<SchemaIndexer>();
    builder.Services.AddSingleton<SchemaRetriever>();

    // Error Handlers
    TextToSqlAgent.Infrastructure.ErrorHandling.ErrorHandlerServiceExtensions.AddErrorHandlers(builder.Services);

    // Plugins
    builder.Services.AddTransient<IntentAnalysisPlugin>();
    builder.Services.AddTransient<SqlGeneratorPlugin>();
    builder.Services.AddTransient<SqlCorrectorPlugin>();

    // Agent
    builder.Services.AddSingleton<TextToSqlAgentOrchestrator>();

    // ============================================
    // 3. API SERVICES
    // ============================================
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    // Database Context for Identity
    var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=identity.db";
    builder.Services.AddDbContext<TextToSqlAgent.API.Data.AppDbContext>(options =>
        options.UseSqlite(connectionString));

    // Identity
    builder.Services.AddIdentity<TextToSqlAgent.API.Data.ApplicationUser, IdentityRole>(options =>
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

    // JWT Authentication
    var jwtIssuer = configuration["Jwt:Issuer"] ?? "TextToSqlAgentAPI";
    var jwtAudience = configuration["Jwt:Audience"] ?? "TextToSqlAgentClient";

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtKey!))
        };
        
        // ✅ SECURITY: Configure JWT events for better error handling
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Error("Authentication failed: {Message}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Log.Debug("Token validated for user: {User}", context.Principal?.Identity?.Name);
                return Task.CompletedTask;
            }
        };
    });

    // ✅ ADD: CORS for API access
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // ✅ ADD: Response caching
    builder.Services.AddResponseCaching();

    // ============================================
    // 4. BUILD AND CONFIGURE
    // ============================================
    var app = builder.Build();

    // ✅ ADD: Global exception handler middleware
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred. Please try again later.\"}");
            Log.Error("Unhandled exception in pipeline");
        });
    });

    // Initialize Database
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TextToSqlAgent.API.Data.AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            logger.LogInformation("Database initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize database");
        }
    }

    // Configure Pipeline
    app.UseHttpsRedirection();
    app.UseCors();
    
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    Log.Information("TextToSqlAgent API started successfully");
    Log.Information("API available at: http://localhost:5000");
    Log.Information("Health check: http://localhost:5000/api/agent/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
