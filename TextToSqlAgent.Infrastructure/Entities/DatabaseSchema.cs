using System;
using System.Collections.Generic;

namespace TextToSqlAgent.Infrastructure.Entities;

/// <summary>
/// Stores database schema information for a connection (cached)
/// </summary>
public class DatabaseSchema
{
    /// <summary>
    /// Unique identifier for this schema record
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Foreign key to the connection this schema belongs to
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Table name
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Schema name (e.g., dbo, public)
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Column name
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Data type (e.g., int, varchar(255))
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this column is nullable
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Whether this column is a primary key
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Whether this column is a foreign key
    /// </summary>
    public bool IsForeignKey { get; set; }

    /// <summary>
    /// Referenced table (if foreign key)
    /// </summary>
    public string? ReferencedTable { get; set; }

    /// <summary>
    /// Referenced column (if foreign key)
    /// </summary>
    public string? ReferencedColumn { get; set; }

    /// <summary>
    /// Default value (if any)
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Column ordinal position
    /// </summary>
    public int OrdinalPosition { get; set; }

    /// <summary>
    /// Whether this column is indexed
    /// </summary>
    public bool IsIndexed { get; set; }

    /// <summary>
    /// Character max length (for string types)
    /// </summary>
    public int? CharacterMaximumLength { get; set; }

    /// <summary>
    /// Numeric precision (for decimal types)
    /// </summary>
    public int? NumericPrecision { get; set; }

    /// <summary>
    /// Numeric scale (for decimal types)
    /// </summary>
    public int? NumericScale { get; set; }

    /// <summary>
    /// Timestamp when this schema was synced
    /// </summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property to the connection
    /// </summary>
    public virtual Connection Connection { get; set; } = null!;
}

/// <summary>
/// Summary of schema for quick listing
/// </summary>
public class SchemaSummary
{
    /// <summary>
    /// Foreign key to the connection
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Total number of tables
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Total number of columns
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// Last sync timestamp
    /// </summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// List of table names
    /// </summary>
    public List<string> Tables { get; set; } = new();
}
