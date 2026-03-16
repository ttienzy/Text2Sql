using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TextToSqlAgent.API.Authorization;

namespace TextToSqlAgent.API.Extensions;

/// <summary>
/// Extension methods for configuring authentication and authorization
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Configure JWT authentication with proper validation parameters
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"];
        var jwtIssuer = configuration["Jwt:Issuer"] ?? "TextToSqlAgentAPI";
        var jwtAudience = configuration["Jwt:Audience"] ?? "TextToSqlAgentClient";

        if (string.IsNullOrEmpty(jwtKey))
        {
            throw new InvalidOperationException("JWT Key is not configured. Please set Jwt:Key in configuration.");
        }

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.SaveToken = true;
            options.RequireHttpsMetadata = false; // Set to true in production
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes clock skew
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            // Configure JWT events for better error handling and logging
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerEvents>>();

                    logger.LogWarning("JWT Authentication failed: {Message}", context.Exception.Message);

                    // Add custom headers for debugging (development only)
                    if (context.HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>().IsDevelopment())
                    {
                        context.Response.Headers.Append("Token-Error", context.Exception.Message);
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerEvents>>();

                    var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    logger.LogDebug("JWT Token validated successfully for user: {UserId}", userId);

                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<JwtBearerEvents>>();

                    logger.LogDebug("JWT Authentication challenge: {Error} - {ErrorDescription}",
                        context.Error, context.ErrorDescription);

                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    // Allow token from query string for WebSocket connections if needed
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }

    /// <summary>
    /// Configure authorization policies and handlers
    /// </summary>
    public static IServiceCollection AddCustomAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(AuthorizationPolicies.ConfigurePolicies);

        // Register authorization handlers
        services.AddScoped<IAuthorizationHandler, ValidUserAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();

        return services;
    }

    /// <summary>
    /// Configure CORS for API access
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? new[] { "http://localhost:3000", "https://localhost:3001" };

                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });

            // Add a more permissive policy for development
            options.AddPolicy("Development", policy =>
            {
                policy.WithOrigins(
                        "http://localhost:5173",
                        "https://localhost:5173",
                        "http://localhost:5174",
                        "https://localhost:5174",
                        "http://localhost:3000",
                        "https://localhost:3001"
                      )
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials()
                      .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        return services;
    }

    /// <summary>
    /// Configure security headers
    /// </summary>
    public static IServiceCollection AddSecurityHeaders(this IServiceCollection services)
    {
        services.AddHsts(options =>
        {
            options.Preload = true;
            options.IncludeSubDomains = true;
            options.MaxAge = TimeSpan.FromDays(365);
        });

        return services;
    }
}