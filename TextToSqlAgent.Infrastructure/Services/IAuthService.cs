using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Services;

/// <summary>
/// Service interface for authentication operations including JWT token generation and refresh token management
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Generates a new JWT access token for the specified user
    /// </summary>
    /// <param name="user">The user to generate the token for</param>
    /// <returns>A tuple containing the access token and its expiration time</returns>
    Task<(string Token, DateTime Expiration)> GenerateAccessTokenAsync(ApplicationUser user);

    /// <summary>
    /// Generates a new refresh token for the specified user
    /// </summary>
    /// <param name="userId">The user ID to generate the refresh token for</param>
    /// <param name="ipAddress">Optional IP address of the client</param>
    /// <param name="userAgent">Optional user agent of the client</param>
    /// <returns>The generated refresh token entity</returns>
    Task<RefreshToken> GenerateRefreshTokenAsync(string userId, string? ipAddress = null, string? userAgent = null);

    /// <summary>
    /// Validates a refresh token and returns the associated user if valid
    /// </summary>
    /// <param name="token">The refresh token to validate</param>
    /// <returns>The user associated with the token if valid, null otherwise</returns>
    Task<ApplicationUser?> ValidateRefreshTokenAsync(string token);

    /// <summary>
    /// Revokes a refresh token
    /// </summary>
    /// <param name="token">The refresh token to revoke</param>
    /// <returns>True if the token was revoked successfully, false otherwise</returns>
    Task<bool> RevokeRefreshTokenAsync(string token);

    /// <summary>
    /// Refreshes an access token using a valid refresh token
    /// </summary>
    /// <param name="refreshToken">The refresh token to use</param>
    /// <returns>A tuple containing the new access token, expiration, and the refresh token</returns>
    Task<(string AccessToken, DateTime Expiration, RefreshToken RefreshToken)?> RefreshAccessTokenAsync(string refreshToken);

    /// <summary>
    /// Gets all active refresh tokens for a user
    /// </summary>
    /// <param name="userId">The user ID to get refresh tokens for</param>
    /// <returns>List of active refresh tokens</returns>
    Task<IEnumerable<RefreshToken>> GetActiveRefreshTokensAsync(string userId);

    /// <summary>
    /// Revokes all refresh tokens for a user (logout from all devices)
    /// </summary>
    /// <param name="userId">The user ID to revoke all tokens for</param>
    /// <returns>Number of tokens revoked</returns>
    Task<int> RevokeAllRefreshTokensAsync(string userId);
}
