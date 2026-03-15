namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Represents a database connection configuration stored by a user
/// </summary>
public class Connection
{
    /// <summary>
    /// Unique identifier for the connection
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Foreign key to the user who owns this connection
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database provider type (sqlserver, postgresql, mysql, sqlite)
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Database host
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Database port
    /// </summary>
    public int Port { get; set; } = 1433;

    /// <summary>
    /// Database name
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Database username
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted password (AES-256)
    /// </summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>
    /// Connection string (reconstructed from fields, also encrypted)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional description for the connection
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is the default connection for the user
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether the connection has been soft-deleted
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Timestamp when the connection was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the connection was last used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Timestamp when schema was last synced
    /// </summary>
    public DateTime? SchemaSyncedAt { get; set; }

    /// <summary>
    /// Navigation property to the user who owns this connection
    /// </summary>
    public virtual ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Navigation property to conversations using this connection
    /// </summary>
    public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    /// <summary>
    /// Navigation property to cached schema information
    /// </summary>
    public virtual ICollection<DatabaseSchema> Schemas { get; set; } = new List<DatabaseSchema>();
}