using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Agent;
using TextToSqlAgent.Core.Tools;
using TextToSqlAgent.Infrastructure.Agent;
using TextToSqlAgent.Infrastructure.Tools;
using TextToSqlAgent.Infrastructure.RAG;
using TextToSqlAgent.Infrastructure.Prompts;
using TextToSqlAgent.Infrastructure.Observability;
using TextToSqlAgent.Infrastructure.Analysis;
using TextToSqlAgent.Infrastructure.Verification;

namespace TextToSqlAgent.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering ReAct Agent and Tools
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register ReAct Agent with all tools
    /// </summary>
    public static IServiceCollection AddReActAgent(this IServiceCollection services)
    {
        // Register Prompt Registry (Phase 3)
        services.AddSingleton<PromptRegistry>();
        services.AddSingleton<PromptOptimizer>();

        // Observability (Phase 4)
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<TelemetryService>();
        services.AddSingleton<HealthCheckService>();

        // Tool Registry (uses service provider to resolve scoped tools)
        services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();

        // Reasoning and Reflection Engines
        services.AddSingleton<IReasoningEngine, ReasoningEngine>();
        services.AddSingleton<IReflectionEngine, ReflectionEngine>();

        // ✅ NEW: LLM Tool Selector for intelligent tool selection
        services.AddSingleton<LLMToolSelector>();

        // Register ReAct Agent (inner implementation)
        services.AddSingleton<ReActAgent>();

        // Register Observable Agent (decorator with telemetry)
        services.AddSingleton<IAgent>(sp =>
        {
            var innerAgent = sp.GetRequiredService<ReActAgent>();
            var telemetry = sp.GetRequiredService<TelemetryService>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ObservableAgent>();

            return new ObservableAgent(innerAgent, telemetry, logger);
        });

        // Register Tools
        services.AddScoped<SchemaExplorerTool>();
        services.AddScoped<SqlGeneratorTool>();
        services.AddScoped<SqlExecutorTool>();
        services.AddScoped<SqlValidatorTool>();

        // Tools are registered as scoped and will be resolved by ServiceProviderToolRegistry

        return services;
    }

    /// <summary>
    /// Register Advanced RAG components (Phase 2)
    /// </summary>
    public static IServiceCollection AddAdvancedRAG(this IServiceCollection services)
    {
        // Register RAG components
        services.AddSingleton<EntityRecognizer>();
        services.AddSingleton<HybridSearchEngine>();
        services.AddSingleton<RelationshipInference>();
        services.AddSingleton<AdvancedSchemaLinker>();

        // Register QueryDecomposer tool
        services.AddScoped<QueryDecomposerTool>();

        // Tools are registered and will be resolved by ServiceProviderToolRegistry

        return services;
    }

    /// <summary>
    /// Register Advanced Features (Phase 5)
    /// </summary>
    public static IServiceCollection AddAdvancedFeatures(this IServiceCollection services)
    {
        // Register Analysis components
        services.AddSingleton<AmbiguityDetector>();
        services.AddSingleton<QueryComplexityAnalyzer>();
        services.AddSingleton<ResultVerifier>();

        // Register Advanced Tools
        services.AddScoped<AmbiguityDetectorTool>();
        services.AddScoped<ComplexityAnalyzerTool>();
        services.AddScoped<ResultVerifierTool>();

        // Tools are registered and will be resolved by ServiceProviderToolRegistry

        return services;
    }

    /// <summary>
    /// Register ReAct Agent with full production features (Phase 7)
    /// Includes: Base Agent → Observable (telemetry) → Cached (performance)
    /// </summary>
    public static IServiceCollection AddReActAgentProduction(this IServiceCollection services)
    {
        // First register base agent and tools
        services.AddReActAgent();
        services.AddAdvancedRAG();
        services.AddAdvancedFeatures();

        // Override IAgent registration with production decorators
        services.AddSingleton<IAgent>(sp =>
        {
            // Base ReAct Agent
            var baseAgent = sp.GetRequiredService<ReActAgent>();

            // Wrap with Observable (telemetry & metrics)
            var telemetry = sp.GetRequiredService<TelemetryService>();
            var metrics = sp.GetRequiredService<MetricsCollector>();
            var observableLogger = sp.GetRequiredService<ILogger<ObservableAgent>>();
            var observableAgent = new ObservableAgent(baseAgent, telemetry, observableLogger);

            // Note: Caching is handled at a different level (query result caching)
            // Agent-level caching would cache the entire reasoning process which may not be desirable

            return observableAgent;
        });

        return services;
    }
}
