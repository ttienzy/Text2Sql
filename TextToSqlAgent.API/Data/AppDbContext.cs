using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TextToSqlAgent.Infrastructure.Entities;

namespace TextToSqlAgent.API.Data;

/// <summary>
/// API Database context that extends the Infrastructure AppDbContext
/// Configured for compatibility with existing Console project entities
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
    /// Refresh tokens for maintaining authenticated sessions
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Token usage tracking for billing
    /// </summary>
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();

    /// <summary>
    /// Database schema cache for connections
    /// </summary>
    public DbSet<DatabaseSchema> DatabaseSchemas => Set<DatabaseSchema>();

    /// <summary>
    /// Background agent jobs for async processing
    /// </summary>
    public DbSet<AgentJob> AgentJobs => Set<AgentJob>();

    /// <summary>
    /// Configures the entity mappings and relationships
    /// Compatible with existing Console project entities from TextToSqlAgent.Infrastructure
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure ApplicationUser (extends IdentityUser, so we only configure additional properties)
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FullName).HasMaxLength(256);
        });

        // Configure Connection
        builder.Entity<Connection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Host).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Database).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EncryptedPassword).IsRequired();
            entity.Property(e => e.ConnectionString).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.IsDefault).HasDefaultValue(false);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            // Indexes for performance
            entity.HasIndex(e => new { e.UserId, e.IsDefault })
                .HasDatabaseName("IX_Connection_UserId_IsDefault");
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_Connection_UserId");

            // Relationships
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
            entity.Property(e => e.IsArchived).HasDefaultValue(false);

            // Indexes for performance
            entity.HasIndex(e => new { e.UserId, e.IsArchived })
                .HasDatabaseName("IX_Conversation_UserId_IsArchived");
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.LastActiveAt);

            // Relationships
            entity.HasOne(e => e.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Connection)
                .WithMany(c => c.Conversations)
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Message
        builder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.SqlQuery).HasMaxLength(10000);
            entity.Property(e => e.Results).HasMaxLength(50000);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.Explanation).HasMaxLength(5000);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.Cost).HasPrecision(18, 6);

            // Indexes for performance
            entity.HasIndex(e => e.ConversationId)
                .HasDatabaseName("IX_Message_ConversationId");
            entity.HasIndex(e => e.CreatedAt);

            // Relationships
            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure RefreshToken
        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).HasMaxLength(512).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.IsRevoked).HasDefaultValue(false);

            // Indexes for performance
            entity.HasIndex(e => e.Token).IsUnique()
                .HasDatabaseName("IX_RefreshToken_Token");
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_RefreshToken_UserId");
            entity.HasIndex(e => e.ExpiresAt);

            // Relationships
            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure TokenUsage
        builder.Entity<TokenUsage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Model).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Cost).HasPrecision(18, 6);

            // Indexes for performance
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Timestamp);

            // Relationships
            entity.HasOne(e => e.User)
                .WithMany(u => u.TokenUsages)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Connection)
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Configure DatabaseSchema
        builder.Entity<DatabaseSchema>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TableName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SchemaName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ColumnName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.DataType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ReferencedTable).HasMaxLength(256);
            entity.Property(e => e.ReferencedColumn).HasMaxLength(256);
            entity.Property(e => e.DefaultValue).HasMaxLength(512);

            // Indexes for performance
            entity.HasIndex(e => e.ConnectionId);
            entity.HasIndex(e => new { e.ConnectionId, e.TableName });
            entity.HasIndex(e => new { e.ConnectionId, e.TableName, e.ColumnName });

            // Relationships
            entity.HasOne(e => e.Connection)
                .WithMany(c => c.Schemas)
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AgentJob
        builder.Entity<AgentJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Question).IsRequired();
            entity.Property(e => e.ConnectionId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.SqlQuery).HasMaxLength(10000);
            entity.Property(e => e.Result);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.Explanation).HasMaxLength(5000);
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.Cost).HasPrecision(18, 6);
            entity.Property(e => e.HangfireJobId).HasMaxLength(100);

            // Indexes for performance
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_AgentJob_UserId");
            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_AgentJob_Status");
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.HangfireJobId)
                .HasDatabaseName("IX_AgentJob_HangfireJobId");

            // Relationships
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Connection>()
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}