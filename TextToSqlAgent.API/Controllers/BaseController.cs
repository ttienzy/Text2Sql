using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using TextToSqlAgent.API.Extensions;

namespace TextToSqlAgent.API.Controllers;

/// <summary>
/// Base controller with common validation and utility methods
/// </summary>
[ApiController]
public abstract class BaseController : ControllerBase
{
    protected readonly ILogger _logger;

    protected BaseController(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get current user ID from claims
    /// </summary>
    protected string? GetCurrentUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Get current user ID and throw if not found
    /// </summary>
    protected string GetRequiredUserId()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }

    /// <summary>
    /// Validate model and return bad request if invalid
    /// </summary>
    protected bool IsModelValid()
    {
        return ModelState.IsValid;
    }

    /// <summary>
    /// Get validation errors from ModelState
    /// </summary>
    protected ActionResult GetValidationErrors()
    {
        var errors = ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
            );

        return this.CreateValidationProblem("One or more validation errors occurred", errors);
    }

    /// <summary>
    /// Handle exceptions and return appropriate response
    /// </summary>
    protected ActionResult HandleException(Exception ex, string operation)
    {
        _logger.LogError(ex, "Error during {Operation}", operation);

        return ex switch
        {
            UnauthorizedAccessException => Unauthorized(new { Message = ex.Message }),
            ArgumentException => BadRequest(new { Message = ex.Message }),
            ValidationException => this.CreateValidationProblem(ex.Message),
            KeyNotFoundException => NotFound(new { Message = ex.Message }),
            InvalidOperationException => this.CreateProblemDetails(ex.Message, 409),
            _ => this.CreateProblemDetails("An unexpected error occurred", 500)
        };
    }
}