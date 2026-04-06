using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TextToSqlAgent.API.Middleware;

/// <summary>
/// Middleware to handle exceptions and return ProblemDetails responses
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred");

        context.Response.ContentType = "application/problem+json";

        var traceId = context.TraceIdentifier;
        // ✅ IMP-5: Propagate correlationId into every error response
        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() ?? traceId : traceId;

        ProblemDetails problemDetails;

        switch (exception)
        {
            case ArgumentNullException argNull:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                    Title = "Bad Request",
                    Detail = argNull.Message,
                    Status = 400,
                    Extensions = { ["traceId"] = traceId, ["correlationId"] = correlationId }
                };
                break;

            case UnauthorizedAccessException unauthorized:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                    Title = "Unauthorized",
                    Detail = unauthorized.Message,
                    Status = 401,
                    Extensions = { ["traceId"] = traceId, ["correlationId"] = correlationId }
                };
                break;

            case KeyNotFoundException notFound:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                    Title = "Not Found",
                    Detail = notFound.Message,
                    Status = 404,
                    Extensions = { ["traceId"] = traceId, ["correlationId"] = correlationId }
                };
                break;

            case InvalidOperationException invalidOp:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                    Title = "Bad Request",
                    Detail = invalidOp.Message,
                    Status = 400,
                    Extensions = { ["traceId"] = traceId, ["correlationId"] = correlationId }
                };
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

                // Hide detailed error in production
                var detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "An unexpected error occurred. Please try again later.";

                problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                    Title = "Internal Server Error",
                    Detail = detail,
                    Status = 500,
                    Extensions = { ["traceId"] = traceId, ["correlationId"] = correlationId }
                };
                break;
        }

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }
}

/// <summary>
/// Extension methods for ProblemDetailsMiddleware
/// </summary>
public static class ProblemDetailsMiddlewareExtensions
{
    public static IApplicationBuilder UseProblemDetailsMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ProblemDetailsMiddleware>();
    }
}
