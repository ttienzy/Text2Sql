using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Infrastructure.Caching;
using TextToSqlAgent.Infrastructure.Security;

namespace TextToSqlAgent.Infrastructure.Extensions;

/// <summary>
/// Extension methods for production-ready features
/// </summary>
public static class ProductionExtensions
{
    /// <summary>
    /// Add caching with Redis
    /// </summary>
    public static IServiceCollection AddProductionCaching(
        this IServiceCollection services,
        string? redisConnectionString = null,
        Action<CacheOptions>? configureCacheOptions = null)
    {
        // Configure cache options
        var cacheOptions = new CacheOptions();
        configureCacheOptions?.Invoke(cacheOptions);
        services.AddSingleton(cacheOptions);

        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            // Use Redis for distributed caching (requires Microsoft.Extensions.Caching.StackExchangeRedis package)
            // services.AddStackExchangeRedisCache(options =>
            // {
            //     options.Configuration = redisConnectionString;
            //     options.InstanceName = "TextToSqlAgent:";
            // });

            // For now, use simple in-memory implementation
            services.AddSingleton<IDistributedCache, SimpleMemoryCache>();
        }
        else
        {
            // Use simple in-memory cache for development
            services.AddSingleton<IDistributedCache, SimpleMemoryCache>();
        }

        // Register cache service
        services.AddSingleton<CacheService>();

        return services;
    }

    /// <summary>
    /// Add security features (rate limiting, SQL injection prevention, cost estimation)
    /// </summary>
    public static IServiceCollection AddProductionSecurity(
        this IServiceCollection services,
        Action<RateLimitOptions>? configureRateLimit = null,
        Action<CostLimits>? configureCostLimits = null)
    {
        // Configure rate limiting
        var rateLimitOptions = new RateLimitOptions();
        configureRateLimit?.Invoke(rateLimitOptions);
        services.AddSingleton(rateLimitOptions);
        services.AddSingleton<RateLimiter>();

        // Configure cost limits
        var costLimits = new CostLimits();
        configureCostLimits?.Invoke(costLimits);
        services.AddSingleton(costLimits);
        services.AddSingleton<QueryCostEstimator>();

        // SQL injection prevention
        services.AddSingleton<SqlInjectionPrevention>();

        return services;
    }

    /// <summary>
    /// Add all production features
    /// </summary>
    public static IServiceCollection AddProductionFeatures(
        this IServiceCollection services,
        string? redisConnectionString = null,
        Action<ProductionOptions>? configure = null)
    {
        var options = new ProductionOptions();
        configure?.Invoke(options);

        // Add caching
        services.AddProductionCaching(
            redisConnectionString,
            opts =>
            {
                opts.EnableCaching = options.EnableCaching;
                opts.DefaultExpiration = options.CacheDefaultExpiration;
                opts.SchemaExpiration = options.CacheSchemaExpiration;
                opts.EmbeddingExpiration = options.CacheEmbeddingExpiration;
                opts.SqlResultExpiration = options.CacheSqlResultExpiration;
            });

        // Add security
        services.AddProductionSecurity(
            opts =>
            {
                opts.EnableRateLimiting = options.EnableRateLimiting;
                opts.MaxRequests = options.RateLimitMaxRequests;
                opts.Window = options.RateLimitWindow;
            },
            opts =>
            {
                opts.MaxComplexityScore = options.MaxComplexityScore;
                opts.MaxEstimatedCost = options.MaxEstimatedCost;
                opts.MaxJoins = options.MaxJoins;
                opts.MaxSubqueries = options.MaxSubqueries;
                opts.MaxExecutionTimeMs = options.MaxExecutionTimeMs;
            });

        return services;
    }
}

/// <summary>
/// Production configuration options
/// </summary>
public class ProductionOptions
{
    // Caching
    public bool EnableCaching { get; set; } = true;
    public TimeSpan CacheDefaultExpiration { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan CacheSchemaExpiration { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan CacheEmbeddingExpiration { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan CacheSqlResultExpiration { get; set; } = TimeSpan.FromMinutes(30);

    // Rate Limiting
    public bool EnableRateLimiting { get; set; } = true;
    public int RateLimitMaxRequests { get; set; } = 100;
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    // Query Cost Limits
    public double MaxComplexityScore { get; set; } = 25.0;
    public double MaxEstimatedCost { get; set; } = 1000.0;
    public int MaxJoins { get; set; } = 8;
    public int MaxSubqueries { get; set; } = 5;
    public int MaxExecutionTimeMs { get; set; } = 30000;
}
