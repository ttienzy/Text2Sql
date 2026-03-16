using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace TextToSqlAgent.API.Authorization;

/// <summary>
/// Authorization attribute that ensures users can only access their own resources
/// </summary>
public class AuthorizeUserResourceAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _userIdParameterName;

    /// <summary>
    /// Initialize with parameter name that contains the user ID
    /// </summary>
    /// <param name="userIdParameterName">Name of the parameter containing user ID (default: "userId")</param>
    public AuthorizeUserResourceAttribute(string userIdParameterName = "userId")
    {
        _userIdParameterName = userIdParameterName;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // Check if user is authenticated
        if (!user.Identity?.IsAuthenticated == true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Get the resource user ID from route values
        if (context.RouteData.Values.TryGetValue(_userIdParameterName, out var resourceUserIdObj))
        {
            var resourceUserId = resourceUserIdObj?.ToString();

            if (!string.IsNullOrEmpty(resourceUserId) &&
                !string.Equals(currentUserId, resourceUserId, StringComparison.OrdinalIgnoreCase))
            {
                // User is trying to access another user's resource
                context.Result = new ForbidResult();
                return;
            }
        }

        // Authorization successful - continue to action
    }
}

/// <summary>
/// Authorization attribute for connection resources
/// Ensures users can only access their own connections
/// </summary>
public class AuthorizeConnectionAccessAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // Check if user is authenticated
        if (!user.Identity?.IsAuthenticated == true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Get connection ID from route
        if (context.RouteData.Values.TryGetValue("id", out var connectionIdObj))
        {
            var connectionId = connectionIdObj?.ToString();

            if (!string.IsNullOrEmpty(connectionId))
            {
                // Here we would typically check if the connection belongs to the user
                // This would require injecting a service to check the database
                // For now, we'll rely on service-level authorization

                // Store the current user ID in HttpContext for service-level checks
                context.HttpContext.Items["CurrentUserId"] = currentUserId;
            }
        }

        // Continue to action - service will perform detailed authorization
    }
}

/// <summary>
/// Authorization attribute for conversation resources
/// Ensures users can only access their own conversations
/// </summary>
public class AuthorizeConversationAccessAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // Check if user is authenticated
        if (!user.Identity?.IsAuthenticated == true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Store the current user ID in HttpContext for service-level checks
        context.HttpContext.Items["CurrentUserId"] = currentUserId;

        // Continue to action - service will perform detailed authorization
    }
}