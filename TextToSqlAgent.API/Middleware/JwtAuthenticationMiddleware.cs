using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TextToSqlAgent.API.Services;

namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// JWT Authentication Middleware for token validation
/// Validates JWT tokens and sets user context for authorized requests
/// </summary>
public class JwtAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtAuthenticationMiddleware> _logger;

    public JwtAuthenticationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<JwtAuthenticationMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var token = ExtractTokenFromHeader(context.Request);

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var principal = await ValidateTokenAsync(token);
                if (principal != null)
                {
                    context.User = principal;
                    _logger.LogDebug("JWT token validated successfully for user: {UserId}",
                        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                }
                else
                {
                    _logger.LogWarning("JWT token validation failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating JWT token");
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Extract JWT token from Authorization header
    /// </summary>
    private static string? ExtractTokenFromHeader(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader))
            return null;

        // Check for Bearer token format
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        return null;
    }

    /// <summary>
    /// Validate JWT token and return ClaimsPrincipal if valid
    /// </summary>
    private async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            // Try environment variable first, then configuration
            var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET") ??
                         _configuration["Jwt:Key"];
            var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ??
                            _configuration["Jwt:Issuer"];
            var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ??
                              _configuration["Jwt:Audience"];

            if (string.IsNullOrEmpty(jwtKey))
            {
                _logger.LogError("JWT Key is not configured. Check JWT_SECRET environment variable or Jwt:Key in configuration.");
                return null;
            }

            var key = Encoding.UTF8.GetBytes(jwtKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes clock skew
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            // Additional validation: ensure it's a JWT token
            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogWarning("Invalid JWT token algorithm");
                return null;
            }

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("JWT token has expired");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("JWT token has invalid signature");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "JWT token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during JWT token validation");
            return null;
        }
    }
}

/// <summary>
/// Extension methods for JWT authentication middleware
/// </summary>
public static class JwtAuthenticationMiddlewareExtensions
{
    /// <summary>
    /// Adds JWT authentication middleware to the pipeline
    /// </summary>
    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<JwtAuthenticationMiddleware>();
    }
}