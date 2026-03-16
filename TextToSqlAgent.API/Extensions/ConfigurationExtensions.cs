using Microsoft.Extensions.Options;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Caching;

namespace TextToSqlAgent.API.Extensions;

/// <summary>
/// Extensions for configuring application configuration services
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds configuration services and validation
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddConfigurationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // Configure and validate configuration objects
        services.ConfigureAndValidate<OpenAIConfig>(configuration, "OpenAI");
        services.ConfigureAndValidate<GeminiConfig>(configuration, "Gemini");
        services.ConfigureAndValidate<DatabaseConfig>(configuration, "Database");
        services.ConfigureAndValidate<AgentConfig>(configuration, "Agent");
        services.ConfigureAndValidate<QdrantConfig>(configuration, "Qdrant");
        services.ConfigureAndValidate<RAGConfig>(configuration, "RAG");

        // Configure JWT options
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<EncryptionOptions>(configuration.GetSection("Encryption"));
        services.Configure<ProductionOptions>(configuration.GetSection("Production"));
        services.Configure<CacheOptions>(configuration.GetSection("Cache"));
        services.Configure<ObservabilityOptions>(configuration.GetSection("Observability"));

        return services;
    }

    /// <summary>
    /// Configures and validates a configuration section
    /// </summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="sectionName">Configuration section name</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection ConfigureAndValidate<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName) where T : class, new()
    {
        services.Configure<T>(configuration.GetSection(sectionName));
        services.AddSingleton<T>(sp => sp.GetRequiredService<IOptions<T>>().Value);
        return services;
    }

    /// <summary>
    /// Validates configuration on startup
    /// </summary>
    /// <param name="app">Web application</param>
    /// <returns>Web application for chaining</returns>
    public static WebApplication ValidateConfigurationOnStartup(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        var validationResult = configService.ValidateConfiguration();

        if (!validationResult.IsValid)
        {
            logger.LogCritical("Configuration validation failed. Application cannot start.");
            foreach (var error in validationResult.Errors)
            {
                logger.LogCritical("Configuration error: {Error}", error);
            }

            throw new InvalidOperationException(
                $"Configuration validation failed with {validationResult.Errors.Count} errors. " +
                "Please check the logs for details.");
        }

        if (validationResult.Warnings.Any())
        {
            logger.LogWarning("Configuration validation completed with {WarningCount} warnings",
                validationResult.Warnings.Count);
        }
        else
        {
            logger.LogInformation("Configuration validation passed successfully");
        }

        return app;
    }
}

/// <summary>
/// JWT configuration options
/// </summary>
public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

/// <summary>
/// Encryption configuration options
/// </summary>
public class EncryptionOptions
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Production configuration options
/// </summary>
public class ProductionOptions
{
    public bool EnableCaching { get; set; } = true;
    public int CacheTTLMinutes { get; set; } = 60;
    public bool EnableRateLimiting { get; set; } = true;
    public int RateLimitMaxRequests { get; set; } = 100;
    public int RateLimitWindowMinutes { get; set; } = 1;
    public bool EnableSqlInjectionPrevention { get; set; } = true;
    public double MaxComplexityScore { get; set; } = 30.0;
    public bool EnableQueryOptimization { get; set; } = true;
    public int MaxQueryExecutionTime { get; set; } = 60;
    public bool EnableQueryPlanAnalysis { get; set; } = true;
    public bool EnableResultVerification { get; set; } = true;
    public string VerificationStrategy { get; set; } = "comprehensive";
    public bool EnableAnomalyDetection { get; set; } = true;
}

/// <summary>
/// Observability configuration options
/// </summary>
public class ObservabilityOptions
{
    public bool EnableMetrics { get; set; } = true;
    public bool EnableTelemetry { get; set; } = true;
    public bool EnableHealthChecks { get; set; } = true;
    public int MetricsRetentionDays { get; set; } = 7;
    public bool EnableDetailedLogging { get; set; } = true;
    public bool EnablePerformanceTracking { get; set; } = true;
    public bool EnableErrorTracking { get; set; } = true;
    public double SampleRate { get; set; } = 1.0;
    public bool EnableDistributedTracing { get; set; } = false;
    public string TracingExporter { get; set; } = "console";
    public bool EnableSpanAttributes { get; set; } = true;
}