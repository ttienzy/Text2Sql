using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TextToSqlAgent.Infrastructure.Configuration;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Services;

/// <summary>
/// Implementation of IAuthService for JWT token generation and refresh token management
/// </summary>
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    // Token expiration times (configurable)
    private TimeSpan AccessTokenExpiration => TimeSpan.FromMinutes(
        _configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes", 180));
    private TimeSpan RefreshTokenExpiration => TimeSpan.FromDays(
        _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 7));

    public AuthService(
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(string Token, DateTime Expiration)> GenerateAccessTokenAsync(ApplicationUser user)
    {
        var jwtKey = _configuration.GetJwtSecret();
        if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
        {
            throw new InvalidOperationException("JWT key is too short. Minimum 32 characters required.");
        }

        var authClaims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // Add user roles as claims
        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            authClaims.Add(new Claim(ClaimTypes.Role, role));
        }

        var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "TextToSqlAgentAPI",
            audience: _configuration["Jwt:Audience"] ?? "TextToSqlAgentClient",
            expires: DateTime.UtcNow.Add(AccessTokenExpiration),
            claims: authClaims,
            signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
        );

        _logger.LogInformation("Generated access token for user: {UserId}", user.Id);

        return (new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);
    }

    /// <inheritdoc/>
    public async Task<RefreshToken> GenerateRefreshTokenAsync(string userId, string? ipAddress = null, string? userAgent = null)
    {
        // Generate a cryptographically secure random token
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var refreshTokenString = Convert.ToBase64String(randomBytes);

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenString,
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.Add(RefreshTokenExpiration),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false,
            IpAddress = ipAddress,
            UserAgent = userAgent?.Length > 512 ? userAgent.Substring(0, 512) : userAgent
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Generated refresh token {TokenId} for user: {UserId}", refreshToken.Id, userId);

        return refreshToken;
    }

    /// <inheritdoc/>
    public async Task<ApplicationUser?> ValidateRefreshTokenAsync(string token)
    {
        var refreshToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null)
        {
            _logger.LogWarning("Refresh token not found");
            return null;
        }

        if (refreshToken.IsRevoked)
        {
            _logger.LogWarning("Refresh token {TokenId} has been revoked", refreshToken.Id);
            return null;
        }

        if (refreshToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh token {TokenId} has expired", refreshToken.Id);
            return null;
        }

        return refreshToken.User;
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeRefreshTokenAsync(string token)
    {
        var refreshToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null)
        {
            _logger.LogWarning("Refresh token not found for revocation");
            return false;
        }

        if (refreshToken.IsRevoked)
        {
            _logger.LogWarning("Refresh token {TokenId} is already revoked", refreshToken.Id);
            return false;
        }

        refreshToken.IsRevoked = true;
        refreshToken.RevokedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Revoked refresh token {TokenId}", refreshToken.Id);

        return true;
    }

    /// <inheritdoc/>
    public async Task<(string AccessToken, DateTime Expiration, RefreshToken RefreshToken)?> RefreshAccessTokenAsync(string refreshTokenString)
    {
        // Validate the refresh token
        var user = await ValidateRefreshTokenAsync(refreshTokenString);
        if (user == null)
        {
            _logger.LogWarning("Invalid refresh token provided");
            return null;
        }

        // Get the existing refresh token
        var existingToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshTokenString);

        if (existingToken == null)
        {
            return null;
        }

        // Generate new access token
        var (newAccessToken, expiration) = await GenerateAccessTokenAsync(user);

        // Revoke the old token and create a new one (token rotation)
        existingToken.IsRevoked = true;
        existingToken.RevokedAt = DateTime.UtcNow;

        var newRefreshToken = await GenerateRefreshTokenAsync(
            user.Id,
            existingToken.IpAddress,
            existingToken.UserAgent
        );

        // Link the old token to the new one
        existingToken.ReplacedByTokenId = newRefreshToken.Id;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Refreshed access token for user: {UserId}", user.Id);

        return (newAccessToken, expiration, newRefreshToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<RefreshToken>> GetActiveRefreshTokensAsync(string userId)
    {
        return await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(rt => rt.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<int> RevokeAllRefreshTokensAsync(string userId)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} refresh tokens for user: {UserId}", activeTokens.Count, userId);

        return activeTokens.Count;
    }
}
