using TextToSqlAgent.API.DTOs;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for handling user authentication and JWT token management
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Register a new user account
    /// </summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Authenticate user and return JWT tokens
    /// </summary>
    Task<AuthResult> LoginAsync(LoginRequest request);

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    Task<AuthResult> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Revoke a refresh token
    /// </summary>
    Task<bool> RevokeTokenAsync(string refreshToken);

    /// <summary>
    /// Validate an access token
    /// </summary>
    Task<bool> ValidateTokenAsync(string token);
}