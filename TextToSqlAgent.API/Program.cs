using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
// using Microsoft.OpenApi.Models;
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

var builder = WebApplication.CreateBuilder(args);

// 1. Setup Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Load Configuration
var configuration = builder.Configuration;

// Load Config Objects
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

// Register Configs
builder.Services.AddSingleton(geminiConfig);
builder.Services.AddSingleton(openAIConfig);
builder.Services.AddSingleton(databaseConfig);
builder.Services.AddSingleton(agentConfig);
builder.Services.AddSingleton(qdrantConfig);
builder.Services.AddSingleton(ragConfig);

// 3. Register Core Services (Copied & Adapted from Console DI)

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

// Agent (Changed from Singleton to Scoped for API per-request isolation if needed, but keeping Singleton for now as it holds cache)
// Note: In a real API, we might want to move Schema Cache to a Distributed Cache (Redis)
builder.Services.AddSingleton<TextToSqlAgentOrchestrator>();

// 4. API Services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// Database Context for Identity
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=identity.db";
builder.Services.AddDbContext<TextToSqlAgent.API.Data.AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Identity
builder.Services.AddIdentity<TextToSqlAgent.API.Data.ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole>()
    .AddEntityFrameworkStores<TextToSqlAgent.API.Data.AppDbContext>()
    .AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SUPER_SECRET_KEY_FOR_DEV_ONLY_12345678";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TextToSqlAgentAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TextToSqlAgentClient";

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
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey))
    };
});

/*
// Swagger/OpenAPI with Auth logic
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TextToSqlAgent API", Version = "v1" });
    
    // Define Security Scheme
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer 12345abcdef\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});
*/

var app = builder.Build();

// 5. Configure Pipeline
/*
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
*/

app.UseHttpsRedirection();

app.UseAuthentication(); // Added Authentication Middleware
app.UseAuthorization();

app.MapControllers();

app.Run();
