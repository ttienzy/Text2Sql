using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Application.Pipelines.DDL;
using TextToSqlAgent.Application.Pipelines.Forbidden;
using TextToSqlAgent.Application.Pipelines.Write;
using TextToSqlAgent.Application.Routing;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering intent-based pipeline services
/// </summary>
public static class IntentPipelineServiceExtensions
{
    /// <summary>
    /// Register all intent-based pipeline services (Phase 1)
    /// </summary>
    public static IServiceCollection AddIntentBasedPipelines(this IServiceCollection services)
    {
        // Intent Classification
        services.AddScoped<IIntentClassifier, IntentClassifier>();

        // Semantic Table Resolution (NEW!)
        services.AddScoped<ISemanticTableResolver, LlmSemanticTableResolver>();

        // Pipelines
        services.AddScoped<IForbiddenPipeline, ForbiddenPipeline>();
        services.AddScoped<IWritePipeline, WritePipeline>();
        services.AddScoped<IDDLPipeline, DDLPipeline>();

        // Response Builder (Singleton - stateless)
        services.AddSingleton<PipelineResponseBuilder>();

        return services;
    }
}
