namespace TextToSqlAgent.Core.Models.DbExplorer;

/// <summary>
/// Enhanced database schema with statistics
/// Extends base DatabaseSchema with additional metadata
/// </summary>
public class EnhancedDatabaseSchema
{
    /// <summary>
    /// Base schema information
    /// </summary>
    public DatabaseSchema BaseSchema { get; set; } = new();

    /// <summary>
    /// Enhanced table information with statistics
    /// </summary>
    public List<EnhancedTableInfo> EnhancedTables { get; set; } = new();

    /// <summary>
    /// AI analysis result (cached)
    /// </summary>
    public DatabaseAnalysis? Analysis { get; set; }

    /// <summary>
    /// When schema was scanned
    /// </summary>
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Schema fingerprint for cache invalidation
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>
/// Enhanced table information with statistics
/// </summary>
public class EnhancedTableInfo
{
    /// <summary>
    /// Table name
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Schema name
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// Number of rows in table
    /// </summary>
    public long RowCount { get; set; }

    /// <summary>
    /// Number of columns
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// Column information
    /// </summary>
    public List<ColumnInfo> Columns { get; set; } = new();

    /// <summary>
    /// Primary keys
    /// </summary>
    public List<string> PrimaryKeys { get; set; } = new();

    /// <summary>
    /// Foreign keys
    /// </summary>
    public List<string> ForeignKeys { get; set; } = new();

    /// <summary>
    /// Indexes on this table
    /// </summary>
    public List<IndexInfo> Indexes { get; set; } = new();

    /// <summary>
    /// Column statistics (null rate, distinct count, etc.)
    /// </summary>
    public Dictionary<string, ColumnStatistics> ColumnStats { get; set; } = new();

    /// <summary>
    /// Table role (from AI analysis)
    /// </summary>
    public TableRole? Role { get; set; }

    /// <summary>
    /// Module this table belongs to
    /// </summary>
    public string? Module { get; set; }
}

/// <summary>
/// Index information
/// </summary>
public class IndexInfo
{
    public string IndexName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
}

/// <summary>
/// Column statistics
/// </summary>
public class ColumnStatistics
{
    /// <summary>
    /// Percentage of NULL values (0-1)
    /// </summary>
    public double NullRate { get; set; }

    /// <summary>
    /// Number of distinct values
    /// </summary>
    public long DistinctCount { get; set; }

    /// <summary>
    /// Minimum value (for numeric/date columns)
    /// </summary>
    public object? MinValue { get; set; }

    /// <summary>
    /// Maximum value (for numeric/date columns)
    /// </summary>
    public object? MaxValue { get; set; }

    /// <summary>
    /// Average value (for numeric columns)
    /// </summary>
    public double? AvgValue { get; set; }
}
