using System;
using System.ComponentModel.DataAnnotations;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Represents a refresh token for maintaining authenticated sessions
/// </summary>
public class RefreshToken
{
    /// <summary>
    /// Unique identifier for the refresh token
    /// </summary>
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The actual token value (hashed for storage)
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the user who owns this refresh token
    /// </summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Date and time when the token expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Indicates whether the token has been revoked
    /// </summary>
    public bool IsRevoked { get; set; } = false;

    /// <summary>
    /// Date and time when the token was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date and time when the token was revoked (if applicable)
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// ID of the token that replaced this one (for token rotation)
    /// </summary>
    public string? ReplacedByTokenId { get; set; }

    /// <summary>
    /// IP address of the client that created this token
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent of the client that created this token
    /// </summary>
    [MaxLength(512)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Navigation property to the user who owns this refresh token
    /// </summary>
    public virtual ApplicationUser User { get; set; } = null!;
}
