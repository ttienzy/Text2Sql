using Microsoft.Extensions.DependencyInjection;
using TextToSqlAgent.Application.Pipelines.DDL;
using TextToSqlAgent.Application.Pipelines.Forbidden;
using TextToSqlAgent.Application.Pipelines.Write;
using TextToSqlAgent.Application.Routing;
using TextToSqlAgent.Application.Services;
using TextToSqlAgent.Application.Services.Advanced;
using TextToSqlAgent.Application.Services.Correction;
using TextToSqlAgent.Application.Services.Validation;
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

        // Query Complexity Analyzer (NEW - Phase 1)
        services.AddScoped<QueryComplexityAnalyzer>();

        // Smart Query Router (NEW - Phase 1)
        services.AddScoped<SmartQueryRouter>();

        // Conversation Context Extractor (NEW - Phase 2)
        services.AddScoped<ConversationContextExtractor>();

        // Semantic SQL Validator (NEW - Phase 3)
        services.AddScoped<SemanticSqlValidator>();

        // Multi-Agent Correction System (NEW - Phase 3)
        services.AddScoped<MultiAgentCorrectionSystem>();

        // Query Decomposer (NEW - Phase 4)
        services.AddScoped<QueryDecomposer>();

        // Confidence Estimator (NEW - Phase 4)
        services.AddScoped<ConfidenceEstimator>();

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
