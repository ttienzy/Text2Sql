using Microsoft.AspNetCore.Mvc;

namespace TextToSqlAgent.API.Extensions;

/// <summary>
/// Extension methods for ControllerBase to simplify ProblemDetails creation
/// </summary>
public static class ControllerExtensions
{
    /// <summary>
    /// Creates a ProblemDetails response with the specified message and status code
    /// </summary>
    public static ObjectResult CreateProblemDetails(this ControllerBase controller, string message, int statusCode)
    {
        var problemDetails = statusCode switch
        {
            400 => ProblemDetailFactory.BadRequest(message),
            401 => ProblemDetailFactory.Unauthorized(message),
            403 => ProblemDetailFactory.Forbidden(message),
            404 => ProblemDetailFactory.NotFound("Resource", "unknown"),
            409 => ProblemDetailFactory.Conflict(message),
            429 => ProblemDetailFactory.TooManyRequests(message, 60),
            503 => ProblemDetailFactory.ServiceUnavailable(message),
            _ => ProblemDetailFactory.InternalError(message)
        };

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode
        };
    }

    /// <summary>
    /// Creates a validation error ProblemDetails response
    /// </summary>
    public static ObjectResult CreateValidationProblem(this ControllerBase controller, string message, Dictionary<string, string[]>? errors = null)
    {
        var problemDetails = ProblemDetailFactory.ValidationError(message, errors);
        return new ObjectResult(problemDetails)
        {
            StatusCode = 400
        };
    }

    /// <summary>
    /// Creates a not found ProblemDetails response
    /// </summary>
    public static ObjectResult CreateNotFoundProblem(this ControllerBase controller, string resourceType, string identifier)
    {
        var problemDetails = ProblemDetailFactory.NotFound(resourceType, identifier);
        return new ObjectResult(problemDetails)
        {
            StatusCode = 404
        };
    }
}