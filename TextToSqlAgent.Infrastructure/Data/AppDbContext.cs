using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.Infrastructure.Data;

/// <summary>
/// Database context for the TextToSqlAgent application
/// Supports PostgreSQL (production) and SQLite (development)
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Database connection configurations
    /// </summary>
    public DbSet<Connection> Connections => Set<Connection>();

    /// <summary>
    /// Chat sessions/conversations
    /// </summary>
    public DbSet<Conversation> Conversations => Set<Conversation>();

    /// <summary>
    /// Individual messages within conversations
    /// </summary>
    public DbSet<Message> Messages => Set<Message>();

    /// <summary>
    /// Token usage tracking for billing
    /// </summary>
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();

    /// <summary>
    /// Refresh tokens for maintaining authenticated sessions
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Database schema cache for connections
    /// </summary>
    public DbSet<DatabaseSchema> DatabaseSchemas => Set<DatabaseSchema>();

    /// <summary>
    /// Background jobs for async agent query processing
    /// </summary>
    public DbSet<AgentJob> AgentJobs => Set<AgentJob>();

    /// <summary>
    /// Configures the entity mappings and relationships
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure ApplicationUser
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).HasMaxLength(256);
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.HasIndex(e => e.NormalizedUserName).IsUnique();
        });

        // Configure Connection
        builder.Entity<Connection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ConnectionString).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.IsDefault).HasDefaultValue(false);
            entity.HasIndex(e => new { e.UserId, e.IsDefault });

            entity.HasOne(e => e.User)
                .WithMany(u => u.Connections)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Conversation
        builder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ContextJson).HasMaxLength(10000);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.LastActiveAt);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.Conversations)
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Configure Message
        builder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.SqlQuery).HasMaxLength(10000);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure TokenUsage
        builder.Entity<TokenUsage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Cost).HasPrecision(18, 6);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.User)
                .WithMany(u => u.TokenUsages)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure RefreshToken
        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).HasMaxLength(512).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AgentJob
        builder.Entity<AgentJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Question).IsRequired();
            entity.Property(e => e.ConnectionId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.SqlQuery).HasMaxLength(10000);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.Result).HasMaxLength(50000);
            entity.Property(e => e.Explanation).HasMaxLength(5000);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.Cost).HasPrecision(18, 6);
            entity.Property(e => e.HangfireJobId).HasMaxLength(100);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}