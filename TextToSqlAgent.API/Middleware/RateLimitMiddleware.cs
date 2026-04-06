using Microsoft.AspNetCore.Http;
using TextToSqlAgent.Infrastructure.Security;

namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// Middleware for rate limiting API requests
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(
        RequestDelegate next,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RateLimiter rateLimiter)
    {
        // Get identifier (user ID, IP address, or API key)
        var identifier = GetIdentifier(context);

        // Check rate limit
        var result = rateLimiter.CheckLimit(identifier);

        // Add rate limit headers
        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = result.ResetAt.ToString("O");

        if (!result.IsAllowed)
        {
            _logger.LogWarning(
                "Rate limit exceeded for {Identifier} on {Path}",
                identifier,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)result.RetryAfter!.Value.TotalSeconds).ToString();

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                message = $"Too many requests. Please retry after {result.RetryAfter.Value.TotalSeconds:F0} seconds.",
                retryAfter = result.RetryAfter.Value.TotalSeconds,
                resetAt = result.ResetAt
            });

            return;
        }

        await _next(context);
    }

    private string GetIdentifier(HttpContext context)
    {
        // Try to get user ID from JWT claims (standard claim types)
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? context.User?.FindFirst("sub")?.Value
                  ?? context.User?.FindFirst("user_id")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        // Try to get API key from header
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return $"apikey:{apiKey}";
        }

        // Fall back to IP address
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ipAddress}";
    }
}

/// <summary>
/// Extension methods for rate limit middleware
/// </summary>
public static class RateLimitMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitMiddleware>();
    }
}
