using Serilog.Context;

namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// Middleware to add correlation IDs to requests for distributed tracing and logging
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private const string CorrelationIdKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get or generate correlation ID
        var correlationId = GetOrGenerateCorrelationId(context);

        // Store in HttpContext for access throughout the request pipeline
        context.Items[CorrelationIdKey] = correlationId;

        // Add to response headers for client tracking
        context.Response.Headers.TryAdd(CorrelationIdHeaderName, correlationId);

        // Add to Serilog LogContext for structured logging
        using (LogContext.PushProperty(CorrelationIdKey, correlationId))
        {
            _logger.LogDebug("Processing request {Method} {Path} with correlation ID {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in request {Method} {Path} with correlation ID {CorrelationId}",
                    context.Request.Method,
                    context.Request.Path,
                    correlationId);
                throw;
            }
            finally
            {
                _logger.LogDebug("Completed request {Method} {Path} with correlation ID {CorrelationId} - Status: {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    correlationId,
                    context.Response.StatusCode);
            }
        }
    }

    private static string GetOrGenerateCorrelationId(HttpContext context)
    {
        // Check if correlation ID is provided in request headers
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationIdHeader))
        {
            var providedId = correlationIdHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(providedId) && IsValidCorrelationId(providedId))
            {
                return providedId;
            }
        }

        // Generate new correlation ID if not provided or invalid
        return GenerateCorrelationId();
    }

    private static bool IsValidCorrelationId(string correlationId)
    {
        // Basic validation: should be alphanumeric and reasonable length
        return !string.IsNullOrWhiteSpace(correlationId) &&
               correlationId.Length <= 50 &&
               correlationId.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    private static string GenerateCorrelationId()
    {
        // Generate a short, readable correlation ID
        return Guid.NewGuid().ToString("N")[..12];
    }
}

/// <summary>
/// Extension methods for adding correlation ID middleware
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    /// Adds correlation ID middleware to the pipeline
    /// </summary>
    /// <param name="builder">Application builder</param>
    /// <returns>Application builder for chaining</returns>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdMiddleware>();
    }
}