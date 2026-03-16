using System.Security.Claims;

namespace TextToSqlAgent.API.Extensions;

/// <summary>
/// Extension methods for HttpContext to simplify user access
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Get the current authenticated user's ID
    /// </summary>
    public static string? GetCurrentUserId(this HttpContext context)
    {
        return context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Get the current authenticated user's email
    /// </summary>
    public static string? GetCurrentUserEmail(this HttpContext context)
    {
        return context.User?.FindFirst(ClaimTypes.Email)?.Value;
    }

    /// <summary>
    /// Get the current authenticated user's name
    /// </summary>
    public static string? GetCurrentUserName(this HttpContext context)
    {
        return context.User?.FindFirst(ClaimTypes.Name)?.Value;
    }

    /// <summary>
    /// Check if the current user is authenticated
    /// </summary>
    public static bool IsUserAuthenticated(this HttpContext context)
    {
        return context.User?.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Get the current user ID from HttpContext items (set by authorization filters)
    /// </summary>
    public static string? GetCurrentUserIdFromItems(this HttpContext context)
    {
        return context.Items["CurrentUserId"]?.ToString();
    }

    /// <summary>
    /// Ensure the current user is authenticated and return user ID
    /// Throws UnauthorizedAccessException if not authenticated
    /// </summary>
    public static string GetRequiredUserId(this HttpContext context)
    {
        var userId = context.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User is not authenticated or user ID is missing");
        }
        return userId;
    }

    /// <summary>
    /// Check if the current user owns the specified resource
    /// </summary>
    public static bool IsResourceOwner(this HttpContext context, string resourceUserId)
    {
        var currentUserId = context.GetCurrentUserId();
        return !string.IsNullOrEmpty(currentUserId) &&
               string.Equals(currentUserId, resourceUserId, StringComparison.OrdinalIgnoreCase);
    }
}