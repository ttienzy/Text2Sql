using TextToSqlAgent.API.DTOs;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Service interface for handling user authentication and JWT token management
/// </summary>
public interface IAuthenticationService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request);
    Task<AuthResult> LoginAsync(LoginRequest request);
    Task<AuthResult> RefreshTokenAsync(string refreshToken);
    Task<bool> RevokeTokenAsync(string refreshToken);
    Task<bool> ValidateTokenAsync(string token);

    /// <summary>Verify Google ID token and return system JWT (find-or-create user).</summary>
    Task<AuthResult> GoogleLoginAsync(string idToken);

    /// <summary>Send 6-digit password-reset code to the user's email.</summary>
    Task<(bool Success, string? Error)> ForgotPasswordAsync(string email);

    /// <summary>Verify 6-digit code and set new password.</summary>
    Task<(bool Success, string? Error)> ResetPasswordAsync(string email, string code, string newPassword);

    /// <summary>Return profile DTO including list of linked OAuth providers.</summary>
    Task<UserProfileResponse?> GetProfileAsync(string userId);
}