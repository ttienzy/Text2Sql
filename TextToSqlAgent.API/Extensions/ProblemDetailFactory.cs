using Microsoft.AspNetCore.Mvc;

namespace TextToSqlAgent.API.Extensions;

/// <summary>
/// Factory for creating ProblemDetails responses according to RFC 7807
/// </summary>
public static class ProblemDetailFactory
{
    /// <summary>
    /// Creates a ProblemDetails response for validation errors
    /// </summary>
    public static ProblemDetails ValidationError(string detail, Dictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Validation Error",
            Detail = detail,
            Status = 400,
            Instance = null
        };

        if (errors != null && errors.Count > 0)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    /// <summary>
    /// Creates a ProblemDetails response for not found errors
    /// </summary>
    public static ProblemDetails NotFound(string resourceType, string identifier)
    {
        return new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Resource Not Found",
            Detail = $"The {resourceType} with identifier '{identifier}' was not found.",
            Status = 404,
            Instance = identifier
        };
    }

    /// <summary>
    /// Creates a ProblemDetails response for unauthorized errors
    /// </summary>
    public static ProblemDetails Unauthorized(string detail = "Authentication is required to access this resource.")
    {
        return new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Unauthorized",
            Detail = detail,
            Status = 401
        };
    }

    /// <summary>
    /// Creates a ProblemDetails response for forbidden errors
    /// </summary>
    public static ProblemDetails Forbidden(string detail = "You do not have permission to access this resource.")
    {
        return new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Forbidden",
            Detail = detail,
            Status = 403
        };
    }

    /// <summary>
    /// Creates a ProblemDetails response for conflict errors
    /// </summary>
    public static ProblemDetails Conflict(string detail, string? resourceId = null)
    {
        return new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Conflict",
            Detail = detail,
            Status = 409,
            Instance = resourceId
        };
    }

    /// <summary>
    /// Creates a ProblemDetails response for internal server errors
    /// </summary>
    public static ProblemDetails InternalError(string? detail = null, string? traceId = null)
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Internal Server Error",
            Detail = detail ?? "An unexpected error occurred. Please try again later.",
            Status = 500
        };

        if (!string.IsNullOrEmpty(traceId))
        {
            problem.Extensions["traceId"] = traceId;
        }

        return problem;
    }

    /// <summary>
    /// Creates a ProblemDetails response for bad request errors
    /// </summary>
    public static ProblemDetails BadRequest(string detail, Dictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Bad Request",
            Detail = detail,
            Status = 400
        };

        if (errors != null && errors.Count > 0)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    /// <summary>
    /// Creates a ProblemDetails response for service unavailable errors
    /// </summary>
    public static ProblemDetails ServiceUnavailable(string detail, DateTime? retryAfter = null)
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Service Unavailable",
            Detail = detail,
            Status = 503
        };

        if (retryAfter.HasValue)
        {
            problem.Extensions["retryAfter"] = retryAfter.Value.ToString("o");
        }

        return problem;
    }

    /// <summary>
    /// Creates a ProblemDetails response for too many requests
    /// </summary>
    public static ProblemDetails TooManyRequests(string detail, int retryAfterSeconds)
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
            Title = "Too Many Requests",
            Detail = detail,
            Status = 429
        };

        problem.Extensions["retryAfter"] = retryAfterSeconds;

        return problem;
    }
}
