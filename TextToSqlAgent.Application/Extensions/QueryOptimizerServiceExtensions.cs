using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Application.Services.QueryOptimizer;

namespace TextToSqlAgent.Application.Extensions;

/// <summary>
/// Extension methods for registering Query Optimizer services
/// </summary>
public static class QueryOptimizerServiceExtensions
{
    /// <summary>
    /// Registers Query Optimizer services
    /// </summary>
    public static IServiceCollection AddQueryOptimizer(this IServiceCollection services)
    {
        // Register core services
        services.AddSingleton<QueryNormalizer>();
        services.AddSingleton<StaticAnalyzer>();
        services.AddSingleton<ComplexityDetector>();
        services.AddScoped<SchemaEnricher>();
        services.AddScoped<QueryOptimizerService>();

        // Sprint 2: Execution Plan & Data Skew services
        services.AddScoped<ExecutionPlanService>();
        services.AddScoped<ColumnStatisticsService>();

        // Phase 4: Token Budget Management
        services.AddSingleton<ContextBudgetManager>();

        return services;
    }
}
