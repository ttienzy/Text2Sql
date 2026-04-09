using System.Diagnostics;
using System.Security.Claims;

namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// Middleware for structured request/response logging.
/// Logs Method, Path, StatusCode, Duration, UserId, CorrelationId.
/// Excludes /health endpoints to reduce noise.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    private static readonly HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/health/ready", "/favicon.ico"
    };

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip logging for noise endpoints
        if (_excludedPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() : context.TraceIdentifier;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();

            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var statusCode = context.Response.StatusCode;
            var level = statusCode >= 500 ? LogLevel.Error
                      : statusCode >= 400 ? LogLevel.Warning
                      : LogLevel.Information;

            _logger.Log(level,
                "[HTTP] {Method} {Path} → {StatusCode} in {ElapsedMs}ms | user={UserId} | corr={CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                statusCode,
                sw.ElapsedMilliseconds,
                userId,
                correlationId);
        }
    }
}

public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
        => builder.UseMiddleware<RequestLoggingMiddleware>();
}
