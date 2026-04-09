using Microsoft.AspNetCore.Mvc;

namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// IMP-2: API versioning middleware.
/// Supports header-based versioning via "X-API-Version" header.
/// Defaults to v1 when no header is present (backward compatible).
/// Stores the resolved version in HttpContext.Items for controllers to inspect.
/// </summary>
public class ApiVersioningMiddleware
{
    private const string VersionHeader = "X-API-Version";
    private const string VersionContextKey = "ApiVersion";
    private static readonly HashSet<string> SupportedVersions = new() { "1", "2" };
    private const string DefaultVersion = "1";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiVersioningMiddleware> _logger;

    public ApiVersioningMiddleware(RequestDelegate next, ILogger<ApiVersioningMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Resolve version from header, default to v1
        var version = DefaultVersion;
        if (context.Request.Headers.TryGetValue(VersionHeader, out var headerValue))
        {
            var requested = headerValue.FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(requested))
            {
                // Strip "v" prefix if present (e.g. "v2" -> "2")
                var normalized = requested.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? requested[1..] : requested;

                if (SupportedVersions.Contains(normalized))
                {
                    version = normalized;
                }
                else
                {
                    _logger.LogWarning("[ApiVersioning] Unsupported version '{Requested}', falling back to v{Default}", requested, DefaultVersion);
                    context.Response.Headers.Append("X-API-Version-Warning", $"Unsupported version '{requested}', using v{DefaultVersion}");
                }
            }
        }

        // Store resolved version in HttpContext.Items
        context.Items[VersionContextKey] = version;

        // Add version to response headers for client transparency
        context.Response.Headers.Append("X-API-Version", version);

        await _next(context);
    }
}

public static class ApiVersioningMiddlewareExtensions
{
    public static IApplicationBuilder UseApiVersioning(this IApplicationBuilder builder)
        => builder.UseMiddleware<ApiVersioningMiddleware>();
}
