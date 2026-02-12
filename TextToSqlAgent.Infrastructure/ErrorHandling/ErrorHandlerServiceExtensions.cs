using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Analysis;

namespace TextToSqlAgent.Infrastructure.ErrorHandling;

/// <summary>
/// Extension methods for registering error handlers
/// </summary>
public static class ErrorHandlerServiceExtensions
{
    /// <summary>
    /// Register all error handlers to DI container
    /// </summary>
    public static IServiceCollection AddErrorHandlers(this IServiceCollection services)
    {
        // Register error analyzer
        services.AddSingleton<SqlErrorAnalyzer>();

        // Register error handlers
        services.AddSingleton<ConnectionErrorHandler>();
        services.AddSingleton<LLMErrorHandler>();
        services.AddSingleton<SqlErrorHandler>();
        services.AddSingleton<VectorDBErrorHandler>();

        return services;
    }
}
