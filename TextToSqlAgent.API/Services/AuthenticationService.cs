using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Implementation of authentication service with JWT token management
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _context;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly IEmailService _emailService;

    // Dictionary to track ongoing refresh operations by token
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshSemaphores = new();

    public AuthenticationService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        AppDbContext context,
        ILogger<AuthenticationService> logger,
        IEmailService emailService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _context = context;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        try
        {
            // Check if user already exists
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                return AuthResult.CreateFailure("User with this email already exists");
            }

            // Create new user
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName,
                EmailConfirmed = true // Auto-confirm for now
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return AuthResult.CreateFailure($"Failed to create user: {errors}");
            }

            _logger.LogInformation("User registered successfully: {Email}", request.Email);

            // Generate tokens for immediate login
            var tokens = await GenerateTokensAsync(user);
            return AuthResult.CreateSuccess(tokens.AccessToken, tokens.RefreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user registration for {Email}", request.Email);
            return AuthResult.CreateFailure("An error occurred during registration");
        }
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                _logger.LogWarning("Login attempt with non-existent email: {Email}", request.Email);
                return AuthResult.CreateFailure("Invalid email or password");
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Failed login attempt for user: {Email}", request.Email);
                return AuthResult.CreateFailure("Invalid email or password");
            }

            var tokens = await GenerateTokensAsync(user);
            _logger.LogInformation("User logged in successfully: {Email}", request.Email);

            return AuthResult.CreateSuccess(tokens.AccessToken, tokens.RefreshToken, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Email}", request.Email);
            return AuthResult.CreateFailure("An error occurred during login");
        }
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken)
    {
        // Use semaphore to prevent concurrent refresh operations for the same token
        var semaphore = _refreshSemaphores.GetOrAdd(refreshToken, _ => new SemaphoreSlim(1, 1));

        try
        {
            await semaphore.WaitAsync();

            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.IsRevoked);

            if (storedToken == null || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                _logger.LogWarning("Invalid or expired refresh token used");
                return AuthResult.CreateFailure("Invalid or expired refresh token");
            }

            // Check if token was already refreshed by another concurrent request
            if (storedToken.IsRevoked)
            {
                _logger.LogWarning("Refresh token was already used: {TokenId}", storedToken.Id);
                return AuthResult.CreateFailure("Refresh token has already been used");
            }

            // Generate new tokens FIRST (before revoking old token)
            var tokens = await GenerateTokensAsync(storedToken.User);

            // Only revoke the old token AFTER successful generation of new tokens
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.ReplacedByTokenId = tokens.RefreshTokenId;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Tokens refreshed successfully for user: {UserId}", storedToken.UserId);
            return AuthResult.CreateSuccess(tokens.AccessToken, tokens.RefreshToken, storedToken.User);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return AuthResult.CreateFailure("An error occurred during token refresh");
        }
        finally
        {
            semaphore.Release();

            // Clean up semaphore after a delay to allow concurrent requests to complete
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
            {
                _refreshSemaphores.TryRemove(refreshToken, out var _);
            });
        }
    }

    public async Task<bool> RevokeTokenAsync(string refreshToken)
    {
        try
        {
            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (storedToken == null)
            {
                return false;
            }

            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Refresh token revoked for user: {UserId}", storedToken.UserId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking refresh token");
            return false;
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            return validatedToken != null;
        }
        catch
        {
            return false;
        }
    }

    // ─── NEW: Google OAuth ────────────────────────────────────────────────────

    public async Task<AuthResult> GoogleLoginAsync(string idToken)
    {
        try
        {
            var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
                           ?? _configuration["Google:ClientId"];

            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("Google ClientId not configured. Check GOOGLE_CLIENT_ID environment variable or Google:ClientId in appsettings.json");
                return AuthResult.CreateFailure("Google authentication is not configured");
            }

            _logger.LogDebug("Google login attempt with ClientId: {ClientId}", clientId.Substring(0, Math.Min(20, clientId.Length)) + "...");

            // Verify the Google ID token
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId]
            });

            var email = payload.Email;
            var googleId = payload.Subject;   // 'sub' claim — stored in AspNetUserLogins.ProviderKey
            var name = payload.Name;
            var picture = payload.Picture;

            // Try to find existing login record first (AspNetUserLogins)
            var user = await _userManager.FindByLoginAsync("Google", googleId);

            if (user == null)
            {
                // No Google login yet — try to find user by email
                user = await _userManager.FindByEmailAsync(email);

                if (user == null)
                {
                    // Brand-new user — create account without password
                    user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        FullName = name,
                        AvatarUrl = picture,
                        EmailConfirmed = true,
                    };

                    var createResult = await _userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                        return AuthResult.CreateFailure($"Failed to create user: {errors}");
                    }
                }
                else
                {
                    // Existing email user — update avatar if not set
                    if (string.IsNullOrEmpty(user.AvatarUrl))
                        user.AvatarUrl = picture;
                }

                // Link Google login to the user (stores in AspNetUserLogins)
                var loginInfo = new UserLoginInfo("Google", googleId, "Google");
                await _userManager.AddLoginAsync(user, loginInfo);
                await _userManager.UpdateAsync(user);
            }

            // Generate system JWT
            var tokens = await GenerateTokensAsync(user);
            _logger.LogInformation("Google login successful for {Email}", email);
            return AuthResult.CreateSuccess(tokens.AccessToken, tokens.RefreshToken, user);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Invalid Google ID token");
            return AuthResult.CreateFailure("Invalid Google token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google login");
            return AuthResult.CreateFailure("An error occurred during Google login");
        }
    }

    // ─── NEW: Forgot Password ─────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> ForgotPasswordAsync(string email)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal whether email exists
                return (true, null);
            }

            // Generate 6-digit numeric OTP
            var code = Random.Shared.Next(100000, 999999).ToString();

            // Store hashed code + 15-min expiry
            user.PasswordResetCode = BCrypt.Net.BCrypt.HashPassword(code);
            user.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(15);
            await _userManager.UpdateAsync(user);

            // Send email
            await _emailService.SendPasswordResetEmailAsync(email, user.FullName ?? email, code);

            _logger.LogInformation("Password reset code sent to {Email}", email);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending password reset code to {Email}", email);
            return (false, "Failed to send reset code. Please try again.");
        }
    }

    // ─── NEW: Reset Password ──────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(string email, string code, string newPassword)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return (false, "Invalid request");

            if (string.IsNullOrEmpty(user.PasswordResetCode) || user.PasswordResetCodeExpiry == null)
                return (false, "No reset code was requested");

            if (user.PasswordResetCodeExpiry < DateTime.UtcNow)
                return (false, "Reset code has expired. Please request a new one.");

            if (!BCrypt.Net.BCrypt.Verify(code, user.PasswordResetCode))
                return (false, "Invalid reset code");

            // Reset password via Identity
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return (false, errors);
            }

            // Clear code after use
            user.PasswordResetCode = null;
            user.PasswordResetCodeExpiry = null;
            await _userManager.UpdateAsync(user);

            _logger.LogInformation("Password reset successful for {Email}", email);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password for {Email}", email);
            return (false, "An error occurred. Please try again.");
        }
    }

    // ─── NEW: Get Profile ─────────────────────────────────────────────────────

    public async Task<UserProfileResponse?> GetProfileAsync(string userId)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return null;

            var logins = await _userManager.GetLoginsAsync(user);
            var hasPassword = await _userManager.HasPasswordAsync(user);

            return new UserProfileResponse
            {
                Id = user.Id,
                Email = user.Email ?? "",
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                HasPassword = hasPassword,
                LinkedProviders = logins.Select(l => l.LoginProvider).ToList(),
                GoogleLinked = logins.Any(l => l.LoginProvider == "Google"),
                FacebookLinked = logins.Any(l => l.LoginProvider == "Facebook"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching profile for user {UserId}", userId);
            return null;
        }
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private async Task<(string AccessToken, string RefreshToken, string RefreshTokenId)> GenerateTokensAsync(ApplicationUser user)
    {
        // Generate access token
        var accessToken = GenerateAccessToken(user);

        // Generate refresh token
        var refreshToken = GenerateRefreshToken();
        var refreshTokenEntity = new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(_configuration.GetValue("Jwt:RefreshTokenExpiryDays", 7))
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        return (accessToken, refreshToken, refreshTokenEntity.Id);
    }

    private string GenerateAccessToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.FullName ?? user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_configuration.GetValue("Jwt:AccessTokenExpiryMinutes", 180)),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
