using Microsoft.AspNetCore.Identity;

namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Extended IdentityUser for application-specific user properties
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Full name of the user
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Navigation property for connections owned by this user
    /// </summary>
    public virtual ICollection<Connection> Connections { get; set; } = new List<Connection>();

    /// <summary>
    /// Navigation property for conversations owned by this user
    /// </summary>
    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    /// <summary>
    /// Navigation property for token usage records
    /// </summary>
    public virtual ICollection<TokenUsage> TokenUsages { get; set; } = new List<TokenUsage>();

    /// <summary>
    /// Navigation property for refresh tokens
    /// </summary>
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}