namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// P1-05: Middleware for adding security headers
/// Protects against common web vulnerabilities
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        ILogger<SecurityHeadersMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // P1-05: Add security headers

        // Prevent clickjacking
        context.Response.Headers.Append("X-Frame-Options", "DENY");

        // Prevent MIME type sniffing
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        // Enable XSS protection
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

        // Referrer policy
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // ✅ CRIT-4 mitigation: Tightened CSP — blocks inline scripts (XSS protection)
        // API-only server: default-src 'none' is correct baseline
        // 'self' for connect-src allows the frontend to call back to this API
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");

        // Permissions Policy (disable unnecessary features)
        context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");

        await _next(context);
    }
}

/// <summary>
/// Extension methods for security headers middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
