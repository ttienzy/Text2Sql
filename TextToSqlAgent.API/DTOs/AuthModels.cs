using System.ComponentModel.DataAnnotations;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.API.Validation;

namespace TextToSqlAgent.API.DTOs;

public class RegisterRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).*$", ErrorMessage = "Password must contain at least one lowercase letter, one uppercase letter, and one number")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Full name must be between 1 and 100 characters")]
    [SafeText(MaxLength = 100, ErrorMessage = "Full name contains invalid characters")]
    public string FullName { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Password is required")]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime AccessTokenExpiration { get; set; }
    public DateTime RefreshTokenExpiration { get; set; }
}

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class AuthResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public AuthResponse? Data { get; set; }

    public static AuthResult CreateSuccess(string accessToken, string refreshToken, ApplicationUser user)
    {
        return new AuthResult
        {
            IsSuccess = true,
            Data = new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                Email = user.Email!,
                FullName = user.FullName ?? user.Email!,
                AccessTokenExpiration = DateTime.UtcNow.AddMinutes(180),
                RefreshTokenExpiration = DateTime.UtcNow.AddDays(7)
            }
        };
    }

    public static AuthResult CreateFailure(string errorMessage)
    {
        return new AuthResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}

// ─── New Auth Request Models ──────────────────────────────────────────────────

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}

// ─── Profile DTO ──────────────────────────────────────────────────────────────

public class UserProfileResponse
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool HasPassword { get; set; }
    public bool GoogleLinked { get; set; }
    public bool FacebookLinked { get; set; }
    public List<string> LinkedProviders { get; set; } = [];
}

