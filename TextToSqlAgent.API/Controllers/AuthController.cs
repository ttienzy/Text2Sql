using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.API.Services;
using TextToSqlAgent.Infrastructure.Services;

namespace TextToSqlAgent.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ITokenQuotaService _tokenQuotaService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthenticationService authenticationService,
        ITokenQuotaService tokenQuotaService,
        ILogger<AuthController> logger)
    {
        _authenticationService = authenticationService;
        _tokenQuotaService = tokenQuotaService;
        _logger = logger;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogInformation("Attempting to register user: {Email}", request.Email);

            var result = await _authenticationService.RegisterAsync(request);

            if (result.IsSuccess)
            {
                _logger.LogInformation("User registered successfully: {Email}", request.Email);
                return Ok(result.Data);
            }

            _logger.LogWarning("Registration failed for {Email}: {Error}", request.Email, result.ErrorMessage);
            return BadRequest(new { Message = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed with exception for {Email}", request.Email);
            return StatusCode(500, new { Message = "An error occurred during registration" });
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogInformation("Attempting login for: {Email}", request.Email);

            var result = await _authenticationService.LoginAsync(request);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Login successful for: {Email}", request.Email);
                return Ok(result.Data);
            }

            _logger.LogWarning("Login failed for {Email}: {Error}", request.Email, result.ErrorMessage);
            return Unauthorized(new { Message = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed with exception for {Email}", request.Email);
            return StatusCode(500, new { Message = "An error occurred during login" });
        }
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogInformation("Attempting token refresh");

            var result = await _authenticationService.RefreshTokenAsync(request.RefreshToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Token refresh successful");
                return Ok(result.Data);
            }

            _logger.LogWarning("Token refresh failed: {Error}", result.ErrorMessage);
            return Unauthorized(new { Message = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed with exception");
            return StatusCode(500, new { Message = "An error occurred during token refresh" });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogInformation("Attempting logout");

            var result = await _authenticationService.RevokeTokenAsync(request.RefreshToken);

            if (result)
            {
                _logger.LogInformation("Logout successful");
                return Ok(new { Message = "Logged out successfully" });
            }

            _logger.LogWarning("Logout failed - token not found or already revoked");
            return BadRequest(new { Message = "Invalid refresh token" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout failed with exception");
            return StatusCode(500, new { Message = "An error occurred during logout" });
        }
    }

    /// <summary>
    /// Get user quota information
    /// </summary>
    [HttpGet("quota")]
    [Authorize]
    [ProducesResponseType(typeof(QuotaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetQuota()
    {
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Get quota from TokenQuotaService
            var tokenQuota = await _tokenQuotaService.GetUserQuotaAsync(userId);

            // Calculate usage percentage
            var usagePercentage = tokenQuota.DailyLimit > 0
                ? Math.Round((double)tokenQuota.UsedToday / tokenQuota.DailyLimit * 100, 2)
                : 0.0;

            var quota = new QuotaResponse
            {
                UserId = userId,
                DailyLimit = tokenQuota.DailyLimit,
                UsedToday = tokenQuota.UsedToday,
                Remaining = tokenQuota.Remaining,
                UsagePercentage = usagePercentage,
                ResetAt = tokenQuota.ResetAt,
                IsUnlimited = tokenQuota.IsUnlimited,
                // Legacy fields for backward compatibility
                TotalTokensUsed = tokenQuota.UsedToday,
                TotalCost = 0.0m,
                MonthlyLimit = tokenQuota.DailyLimit * 30,
                MonthlyUsed = tokenQuota.UsedToday,
                DailyUsed = tokenQuota.UsedToday,
                LastResetDate = tokenQuota.ResetAt.Date.AddDays(-1),
                NextResetDate = tokenQuota.ResetAt.Date
            };

            return Ok(quota);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving quota information for user {UserId}",
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
            return StatusCode(500, new { Message = "An error occurred while retrieving quota information" });
        }
    }

    // ─── Google OAuth ─────────────────────────────────────────────────────────

    /// <summary>
    /// Exchange a Google ID Token for a system JWT
    /// </summary>
    [HttpPost("google")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var result = await _authenticationService.GoogleLoginAsync(request.IdToken);
            if (result.IsSuccess) return Ok(result.Data);
            return BadRequest(new { Message = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google login failed");
            return StatusCode(500, new { Message = "An error occurred during Google login" });
        }
    }

    // ─── Profile ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get current user's profile including linked OAuth providers
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var profile = await _authenticationService.GetProfileAsync(userId);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    // ─── Forgot Password ──────────────────────────────────────────────────────

    /// <summary>
    /// Send a 6-digit password-reset code to the user's email
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var (success, error) = await _authenticationService.ForgotPasswordAsync(request.Email);
            if (!success) return BadRequest(new { Message = error });
            // Always return 200 to avoid email enumeration
            return Ok(new { Message = "If that email exists, a reset code has been sent." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in forgot-password for {Email}", request.Email);
            return StatusCode(500, new { Message = "An error occurred. Please try again." });
        }
    }

    // ─── Reset Password ───────────────────────────────────────────────────────

    /// <summary>
    /// Verify the 6-digit code and set a new password
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var (success, error) = await _authenticationService.ResetPasswordAsync(
                request.Email, request.Code, request.NewPassword);

            if (!success) return BadRequest(new { Message = error });
            return Ok(new { Message = "Password reset successfully. You can now log in." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reset-password for {Email}", request.Email);
            return StatusCode(500, new { Message = "An error occurred. Please try again." });
        }
    }
}


/// <summary>
/// Response model for quota information
/// </summary>
public class QuotaResponse
{
    public string UserId { get; set; } = string.Empty;

    // New fields matching TokenQuota
    public int DailyLimit { get; set; }
    public int UsedToday { get; set; }
    public int Remaining { get; set; }
    public double UsagePercentage { get; set; }
    public DateTime ResetAt { get; set; }
    public bool IsUnlimited { get; set; }

    // Legacy fields for backward compatibility
    public int TotalTokensUsed { get; set; }
    public decimal TotalCost { get; set; }
    public int MonthlyLimit { get; set; }
    public int MonthlyUsed { get; set; }
    public int DailyUsed { get; set; }
    public DateTime LastResetDate { get; set; }
    public DateTime NextResetDate { get; set; }
}