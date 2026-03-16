using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TextToSqlAgent.API.DTOs;
using TextToSqlAgent.Infrastructure.Entities;
using TextToSqlAgent.API.Data;
using Microsoft.EntityFrameworkCore;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Implementation of authentication service with JWT token management
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<TextToSqlAgent.Infrastructure.Entities.ApplicationUser> _userManager;
    private readonly SignInManager<TextToSqlAgent.Infrastructure.Entities.ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _context;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        UserManager<TextToSqlAgent.Infrastructure.Entities.ApplicationUser> userManager,
        SignInManager<TextToSqlAgent.Infrastructure.Entities.ApplicationUser> signInManager,
        IConfiguration configuration,
        AppDbContext context,
        ILogger<AuthenticationService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _context = context;
        _logger = logger;
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
            var user = new TextToSqlAgent.Infrastructure.Entities.ApplicationUser
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
        try
        {
            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.IsRevoked);

            if (storedToken == null || storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                _logger.LogWarning("Invalid or expired refresh token used");
                return AuthResult.CreateFailure("Invalid or expired refresh token");
            }

            // Revoke the old token
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;

            // Generate new tokens
            var tokens = await GenerateTokensAsync(storedToken.User);

            // Set replacement reference
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

    private async Task<(string AccessToken, string RefreshToken, string RefreshTokenId)> GenerateTokensAsync(TextToSqlAgent.Infrastructure.Entities.ApplicationUser user)
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

    private string GenerateAccessToken(TextToSqlAgent.Infrastructure.Entities.ApplicationUser user)
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