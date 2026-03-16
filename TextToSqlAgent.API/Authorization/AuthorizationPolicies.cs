using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace TextToSqlAgent.API.Authorization;

/// <summary>
/// Authorization policy constants and configuration
/// </summary>
public static class AuthorizationPolicies
{
    // Policy names
    public const string RequireAuthenticatedUser = "RequireAuthenticatedUser";
    public const string RequireValidUser = "RequireValidUser";
    public const string ResourceOwner = "ResourceOwner";

    /// <summary>
    /// Configure authorization policies
    /// </summary>
    public static void ConfigurePolicies(AuthorizationOptions options)
    {
        // Basic authenticated user policy
        options.AddPolicy(RequireAuthenticatedUser, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(ClaimTypes.NameIdentifier);
            policy.RequireClaim(ClaimTypes.Email);
        });

        // Valid user with additional claims validation
        options.AddPolicy(RequireValidUser, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(ClaimTypes.NameIdentifier);
            policy.RequireClaim(ClaimTypes.Email);
            policy.Requirements.Add(new ValidUserRequirement());
        });

        // Resource owner policy for user-specific resources
        options.AddPolicy(ResourceOwner, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(ClaimTypes.NameIdentifier);
            policy.Requirements.Add(new ResourceOwnerRequirement());
        });
    }
}

/// <summary>
/// Requirement for valid user validation
/// </summary>
public class ValidUserRequirement : IAuthorizationRequirement
{
    public ValidUserRequirement()
    {
    }
}

/// <summary>
/// Requirement for resource ownership validation
/// </summary>
public class ResourceOwnerRequirement : IAuthorizationRequirement
{
    public ResourceOwnerRequirement()
    {
    }
}

/// <summary>
/// Authorization handler for valid user requirement
/// </summary>
public class ValidUserAuthorizationHandler : AuthorizationHandler<ValidUserRequirement>
{
    private readonly ILogger<ValidUserAuthorizationHandler> _logger;

    public ValidUserAuthorizationHandler(ILogger<ValidUserAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidUserRequirement requirement)
    {
        var user = context.User;

        // Check if user is authenticated
        if (!user.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("User is not authenticated");
            context.Fail();
            return Task.CompletedTask;
        }

        // Validate required claims
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = user.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("User missing required claims - UserId: {UserId}, Email: {Email}",
                userId, email);
            context.Fail();
            return Task.CompletedTask;
        }

        // Additional validation: check if user ID is a valid GUID
        if (!Guid.TryParse(userId, out _))
        {
            _logger.LogWarning("Invalid user ID format: {UserId}", userId);
            context.Fail();
            return Task.CompletedTask;
        }

        _logger.LogDebug("Valid user requirement satisfied for user: {UserId}", userId);
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Authorization handler for resource ownership requirement
/// </summary>
public class ResourceOwnerAuthorizationHandler : AuthorizationHandler<ResourceOwnerRequirement>
{
    private readonly ILogger<ResourceOwnerAuthorizationHandler> _logger;

    public ResourceOwnerAuthorizationHandler(ILogger<ResourceOwnerAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
        var user = context.User;

        // Check if user is authenticated
        if (!user.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("User is not authenticated for resource access");
            context.Fail();
            return Task.CompletedTask;
        }

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User missing NameIdentifier claim for resource access");
            context.Fail();
            return Task.CompletedTask;
        }

        // Get resource user ID from route values or query parameters
        var httpContext = context.Resource as HttpContext;
        if (httpContext != null)
        {
            var resourceUserId = GetResourceUserId(httpContext);

            if (!string.IsNullOrEmpty(resourceUserId))
            {
                // Check if the authenticated user owns the resource
                if (!string.Equals(userId, resourceUserId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("User {UserId} attempted to access resource owned by {ResourceUserId}",
                        userId, resourceUserId);
                    context.Fail();
                    return Task.CompletedTask;
                }
            }
        }

        _logger.LogDebug("Resource owner requirement satisfied for user: {UserId}", userId);
        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Extract resource user ID from HTTP context
    /// This can be extended to handle different resource types
    /// </summary>
    private static string? GetResourceUserId(HttpContext httpContext)
    {
        // Try to get from route values (e.g., /api/users/{userId}/connections)
        if (httpContext.Request.RouteValues.TryGetValue("userId", out var routeUserId))
        {
            return routeUserId?.ToString();
        }

        // Try to get from query parameters
        if (httpContext.Request.Query.TryGetValue("userId", out var queryUserId))
        {
            return queryUserId.FirstOrDefault();
        }

        // For resources that don't explicitly specify user ID in URL,
        // the authorization will be handled at the service level
        return null;
    }
}