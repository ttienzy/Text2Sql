using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Reflection;

namespace TextToSqlAgent.API.Extensions;

/// <summary>
/// Extensions for configuring structured logging with Serilog
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Configures Serilog with environment-specific settings
    /// </summary>
    /// <param name="builder">Host builder</param>
    /// <returns>Host builder for chaining</returns>
    public static IHostBuilder ConfigureStructuredLogging(this IHostBuilder builder)
    {
        return builder.UseSerilog((context, services, configuration) =>
        {
            var environment = context.HostingEnvironment;
            var config = context.Configuration;

            // Base configuration
            configuration
                .ReadFrom.Configuration(config)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "TextToSqlAgent.API")
                .Enrich.WithProperty("Environment", environment.EnvironmentName)
                .Enrich.WithProperty("Version", GetApplicationVersion())
                .Enrich.WithMachineName()
                .Enrich.WithThreadId();

            // Environment-specific configuration
            ConfigureForEnvironment(configuration, environment, config);

            // Add correlation ID enricher for request tracking
            configuration.Enrich.With<CorrelationIdEnricher>();
        });
    }

    private static void ConfigureForEnvironment(
        LoggerConfiguration configuration,
        IHostEnvironment environment,
        IConfiguration config)
    {
        if (environment.IsDevelopment())
        {
            ConfigureDevelopmentLogging(configuration, config);
        }
        else if (environment.IsStaging())
        {
            ConfigureStagingLogging(configuration, config);
        }
        else if (environment.IsProduction())
        {
            ConfigureProductionLogging(configuration, config);
        }
        else
        {
            // Default configuration for unknown environments
            ConfigureDefaultLogging(configuration, config);
        }
    }

    private static void ConfigureDevelopmentLogging(LoggerConfiguration configuration, IConfiguration config)
    {
        configuration
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console(
                // ✅ SIMPLIFIED: Remove {Properties:j} to avoid duplicate logs
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/api-dev-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 3,
                // Keep full properties in file for debugging
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}");
    }

    private static void ConfigureStagingLogging(LoggerConfiguration configuration, IConfiguration config)
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("TextToSqlAgent", LogEventLevel.Information)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/api-staging-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatter: new JsonFormatter(),
                restrictedToMinimumLevel: LogEventLevel.Information);
    }

    private static void ConfigureProductionLogging(LoggerConfiguration configuration, IConfiguration config)
    {
        configuration
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Error)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("TextToSqlAgent", LogEventLevel.Information)
            .WriteTo.Console(
                formatter: new JsonFormatter(),
                restrictedToMinimumLevel: LogEventLevel.Warning)
            .WriteTo.File(
                path: "logs/api-prod-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                formatter: new JsonFormatter(),
                restrictedToMinimumLevel: LogEventLevel.Information);

        // Add additional production sinks if configured
        var enableMetrics = config.GetValue<bool>("Observability:EnableMetrics");
        if (enableMetrics)
        {
            // Could add metrics sink here (e.g., Seq, Elasticsearch, etc.)
        }
    }

    private static void ConfigureDefaultLogging(LoggerConfiguration configuration, IConfiguration config)
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/api-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7);
    }

    private static string GetApplicationVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0";
    }
}

/// <summary>
/// Enricher to add correlation IDs to log entries for request tracking
/// </summary>
public class CorrelationIdEnricher : ILogEventEnricher
{
    private const string CorrelationIdPropertyName = "CorrelationId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = GetCorrelationId();
        if (!string.IsNullOrEmpty(correlationId))
        {
            var property = propertyFactory.CreateProperty(CorrelationIdPropertyName, correlationId);
            logEvent.AddPropertyIfAbsent(property);
        }
    }

    private static string? GetCorrelationId()
    {
        // Try to get correlation ID from HTTP context
        var httpContext = GetHttpContext();
        if (httpContext != null)
        {
            // Check if correlation ID is already set
            if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId))
            {
                return correlationId?.ToString();
            }

            // Generate new correlation ID if not present
            var newCorrelationId = Guid.NewGuid().ToString("N")[..8];
            httpContext.Items["CorrelationId"] = newCorrelationId;
            return newCorrelationId;
        }

        return null;
    }

    private static HttpContext? GetHttpContext()
    {
        try
        {
            // This is a simplified approach - in a real application you might want to use IHttpContextAccessor
            return null; // Will be enhanced when middleware is added
        }
        catch
        {
            return null;
        }
    }
}