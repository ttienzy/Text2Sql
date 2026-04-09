using System;
using System.ComponentModel.DataAnnotations;
using TextToSqlAgent.API.Validation;

namespace TextToSqlAgent.API.DTOs;

/// <summary>
/// Request model for creating a new database connection
/// </summary>
public class CreateConnectionRequest
{
    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    [Required(ErrorMessage = "Connection name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Connection name must be between 1 and 100 characters")]
    [SafeText(MaxLength = 100, ErrorMessage = "Connection name contains invalid characters")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database provider (sqlserver, postgresql, mysql, sqlite)
    /// </summary>
    [Required(ErrorMessage = "Database provider is required")]
    [ValidDatabaseProvider(ErrorMessage = "Invalid database provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Database host
    /// </summary>
    [Required(ErrorMessage = "Database host is required")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "Host must be between 1 and 255 characters")]
    [SafeText(MaxLength = 255, ErrorMessage = "Host contains invalid characters")]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Database port
    /// </summary>
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
    public int Port { get; set; } = 1433;

    /// <summary>
    /// Database name
    /// </summary>
    [Required(ErrorMessage = "Database name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Database name must be between 1 and 100 characters")]
    [SafeIdentifier(ErrorMessage = "Database name contains invalid characters")]
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Database username
    /// </summary>
    [Required(ErrorMessage = "Username is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 100 characters")]
    [SafeText(MaxLength = 100, ErrorMessage = "Username contains invalid characters")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Database password (will be encrypted)
    /// </summary>
    [Required(ErrorMessage = "Password is required")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Password is required")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional connection description
    /// </summary>
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    [SafeText(MaxLength = 500, ErrorMessage = "Description contains invalid characters")]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this should be the default connection
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// System domain for AI context (E-commerce, ERP, CRM, etc.)
    /// </summary>
    [StringLength(100, ErrorMessage = "System domain cannot exceed 100 characters")]
    public string? SystemDomain { get; set; }

    /// <summary>
    /// Naming convention notes for AI interpretation
    /// </summary>
    [StringLength(500, ErrorMessage = "Naming convention notes cannot exceed 500 characters")]
    public string? NamingConventionNotes { get; set; }

    /// <summary>
    /// Business context description for better AI understanding
    /// </summary>
    [StringLength(1000, ErrorMessage = "Business context cannot exceed 1000 characters")]
    public string? BusinessContext { get; set; }
}

/// <summary>
/// Request model for updating an existing connection
/// </summary>
public class UpdateConnectionRequest
{
    /// <summary>
    /// User-defined name for the connection
    /// </summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database host
    /// </summary>
    [Required, MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Database port
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 1433;

    /// <summary>
    /// Database name
    /// </summary>
    [Required, MaxLength(100)]
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Database username
    /// </summary>
    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// New password (leave empty to keep existing)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional connection description
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this should be the default connection
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// System domain for AI context
    /// </summary>
    [MaxLength(100)]
    public string? SystemDomain { get; set; }

    /// <summary>
    /// Naming convention notes for AI interpretation
    /// </summary>
    [MaxLength(500)]
    public string? NamingConventionNotes { get; set; }

    /// <summary>
    /// Business context description
    /// </summary>
    [MaxLength(1000)]
    public string? BusinessContext { get; set; }
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

    /// <summary>
    /// Whether the connection is currently connected/available
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// System domain for AI context
    /// </summary>
    public string? SystemDomain { get; set; }

    /// <summary>
    /// Naming convention notes for AI interpretation
    /// </summary>
    public string? NamingConventionNotes { get; set; }

    /// <summary>
    /// Business context description
    /// </summary>
    public string? BusinessContext { get; set; }
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
    /// Response time for the connection test
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    /// Database version (if successful)
    /// </summary>
    public string? DatabaseVersion { get; set; }

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
    /// Last sync timestamp
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Whether sync is currently in progress
    /// </summary>
    public bool IsInProgress { get; set; }

    /// <summary>
    /// Last error message if sync failed
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Number of tables synced
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Whether the schema is currently synced
    /// </summary>
    public bool IsSynced { get; set; }
}
