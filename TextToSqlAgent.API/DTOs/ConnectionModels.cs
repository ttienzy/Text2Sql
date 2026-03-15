using System;
using System.ComponentModel.DataAnnotations;

namespace TextToSqlAgent.API.DTOs;

/// <summary>
/// Request model for creating a new database connection
/// </summary>
public class CreateConnectionRequest
{
    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database provider (sqlserver, postgresql, mysql, sqlite)
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Database host
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Database port
    /// </summary>
    public int Port { get; set; } = 1433;

    /// <summary>
    /// Database name
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Database username
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Database password (will be encrypted)
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional connection description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this should be the default connection
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Request model for updating an existing connection
/// </summary>
public class UpdateConnectionRequest
{
    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    [StringLength(100)]
    public string? Name { get; set; }

    /// <summary>
    /// Database host
    /// </summary>
    [StringLength(255)]
    public string? Host { get; set; }

    /// <summary>
    /// Database port
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Database name
    /// </summary>
    [StringLength(100)]
    public string? Database { get; set; }

    /// <summary>
    /// Database username
    /// </summary>
    [StringLength(100)]
    public string? Username { get; set; }

    /// <summary>
    /// New password (leave empty to keep existing)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional connection description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this should be the default connection
    /// </summary>
    public bool? IsDefault { get; set; }
}

/// <summary>
/// Response model for connection details (password is NEVER returned)
/// </summary>
public class ConnectionResponse
{
    /// <summary>
    /// Unique identifier for the connection
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database provider
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Database host
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Database port
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Database name
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Database username
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Optional connection description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is the default connection
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Timestamp when the connection was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the connection was last used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Whether the connection has been soft-deleted
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Schema sync status
    /// </summary>
    public SchemaSyncStatus? SchemaSync { get; set; }
}

/// <summary>
/// Response model for connection list (lightweight)
/// </summary>
public class ConnectionListItem
{
    /// <summary>
    /// Unique identifier for the connection
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database provider
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Database host
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Database name
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the default connection
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Timestamp when the connection was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the connection was last used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// Response for testing a connection
/// </summary>
public class TestConnectionResult
{
    /// <summary>
    /// Whether the connection was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Latency in milliseconds (if successful)
    /// </summary>
    public long? LatencyMs { get; set; }

    /// <summary>
    /// Server version (if successful)
    /// </summary>
    public string? ServerVersion { get; set; }

    /// <summary>
    /// Error message (if failed)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when the test was performed
    /// </summary>
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Schema sync status for a connection
/// </summary>
public class SchemaSyncStatus
{
    /// <summary>
    /// Whether schema has been synced
    /// </summary>
    public bool IsSynced { get; set; }

    /// <summary>
    /// Last sync timestamp
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Number of tables synced
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Number of columns synced
    /// </summary>
    public int ColumnCount { get; set; }
}
