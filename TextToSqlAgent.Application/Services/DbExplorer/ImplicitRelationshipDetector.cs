using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Detects implicit foreign key relationships using metadata-only analysis
/// No data queries - only naming patterns, data types, and statistics
/// </summary>
public class ImplicitRelationshipDetector
{
    private readonly ILogger<ImplicitRelationshipDetector> _logger;

    // Common FK naming patterns (case-insensitive)
    private static readonly List<string> FkPatterns = new()
    {
        @"^{table}Id$",           // CustomerId
        @"^{table}_ID$",          // Customer_ID
        @"^{table}ID$",           // CustomerID
        @"^Ma{table}$",           // MaKhachHang (Vietnamese)
        @"^{table}Code$",         // CustomerCode
        @"^{table}Key$",          // CustomerKey
        @"^ID{table}$",           // IDCustomer
        @"^{table}No$",           // CustomerNo
        @"^{table}Ref$",          // CustomerRef
        @"^{table}FK$",           // CustomerFK
    };

    // Common abbreviations (Vietnamese)
    private static readonly Dictionary<string, string> VietnameseAbbreviations = new()
    {
        { "KH", "KhachHang" },
        { "NV", "NhanVien" },
        { "SP", "SanPham" },
        { "DH", "DonHang" },
        { "HD", "HoaDon" },
        { "DM", "DanhMuc" },
        { "NCC", "NhaCungCap" },
        { "KHO", "Kho" },
        { "PX", "PhieuXuat" },
        { "PN", "PhieuNhap" },
    };

    public ImplicitRelationshipDetector(ILogger<ImplicitRelationshipDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detect implicit foreign keys for a table (metadata-only)
    /// </summary>
    public List<ImplicitRelationship> DetectImplicitForeignKeys(
        EnhancedTableInfo table,
        EnhancedDatabaseSchema schema)
    {
        var results = new List<ImplicitRelationship>();

        _logger.LogInformation("[ImplicitFK] Analyzing table {TableName} for implicit relationships", table.TableName);

        // Get all potential parent tables (exclude self and system tables)
        var potentialParents = schema.EnhancedTables
            .Where(t => t.TableName != table.TableName)
            .Where(t => !IsSystemTable(t.TableName))
            .ToList();

        // Analyze each column in the table
        foreach (var column in table.Columns)
        {
            // Skip if already has explicit FK
            if (column.IsForeignKey)
                continue;

            // Skip if is primary key (usually not a FK)
            if (column.IsPrimaryKey)
                continue;

            // Try to find matching parent table
            var matches = FindPotentialParents(column, table, potentialParents, schema);

            foreach (var match in matches)
            {
                // Calculate confidence score
                var confidence = CalculateConfidence(match);

                // Only include if confidence is above threshold (0.6)
                if (confidence >= 0.6)
                {
                    results.Add(new ImplicitRelationship
                    {
                        FromTable = table.TableName,
                        FromColumn = column.ColumnName,
                        ToTable = match.ParentTable.TableName,
                        ToColumn = match.ParentColumn.ColumnName,
                        Confidence = confidence,
                        DetectionMethod = match.Method,
                        Reason = match.Reason,
                        RequiresDataValidation = confidence < 0.85
                    });

                    _logger.LogInformation(
                        "[ImplicitFK] Found: {FromTable}.{FromColumn} → {ToTable}.{ToColumn} (confidence: {Confidence:P0}, method: {Method})",
                        table.TableName, column.ColumnName, match.ParentTable.TableName, match.ParentColumn.ColumnName, confidence, match.Method);
                }
            }
        }

        _logger.LogInformation("[ImplicitFK] Found {Count} implicit relationships for {TableName}", results.Count, table.TableName);

        return results;
    }

    /// <summary>
    /// Find potential parent tables for a column
    /// </summary>
    private List<FkMatch> FindPotentialParents(
        ColumnInfo column,
        EnhancedTableInfo childTable,
        List<EnhancedTableInfo> potentialParents,
        EnhancedDatabaseSchema schema)
    {
        var matches = new List<FkMatch>();

        foreach (var parentTable in potentialParents)
        {
            // Method 1: Exact naming pattern match
            var namingMatch = CheckNamingPattern(column, parentTable);
            if (namingMatch != null)
            {
                matches.Add(namingMatch);
                continue; // Don't check other methods if exact match found
            }

            // Method 2: Column name contains table name
            var containsMatch = CheckColumnContainsTableName(column, parentTable);
            if (containsMatch != null)
            {
                matches.Add(containsMatch);
            }

            // Method 3: Vietnamese abbreviation match
            var abbreviationMatch = CheckVietnameseAbbreviation(column, parentTable);
            if (abbreviationMatch != null)
            {
                matches.Add(abbreviationMatch);
            }
        }

        // Filter matches by data type compatibility
        matches = matches.Where(m => IsDataTypeCompatible(column, m.ParentColumn)).ToList();

        // Filter by row count logic (child rows should be <= parent rows)
        matches = matches.Where(m => IsRowCountLogical(childTable, m.ParentTable)).ToList();

        return matches;
    }

    /// <summary>
    /// Check if column name matches FK naming patterns
    /// </summary>
    private FkMatch? CheckNamingPattern(ColumnInfo column, EnhancedTableInfo parentTable)
    {
        var columnName = column.ColumnName;
        var tableName = parentTable.TableName;

        // Try each pattern
        foreach (var pattern in FkPatterns)
        {
            var regex = new Regex(
                pattern.Replace("{table}", Regex.Escape(tableName)),
                RegexOptions.IgnoreCase);

            if (regex.IsMatch(columnName))
            {
                // Find primary key column in parent table
                var pkColumn = parentTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                if (pkColumn != null)
                {
                    return new FkMatch
                    {
                        ParentTable = parentTable,
                        ParentColumn = pkColumn,
                        Method = "naming_pattern",
                        Reason = $"Column name '{columnName}' matches FK pattern for table '{tableName}'",
                        NamingScore = 1.0,
                        TypeScore = 0.0, // Will be calculated later
                        RowCountScore = 0.0
                    };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Check if column name contains table name
    /// </summary>
    private FkMatch? CheckColumnContainsTableName(ColumnInfo column, EnhancedTableInfo parentTable)
    {
        var columnName = column.ColumnName.ToLower();
        var tableName = parentTable.TableName.ToLower();

        // Check if column contains table name
        if (columnName.Contains(tableName) && columnName != tableName)
        {
            // Find primary key column in parent table
            var pkColumn = parentTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pkColumn != null)
            {
                return new FkMatch
                {
                    ParentTable = parentTable,
                    ParentColumn = pkColumn,
                    Method = "name_contains",
                    Reason = $"Column name '{column.ColumnName}' contains table name '{parentTable.TableName}'",
                    NamingScore = 0.7,
                    TypeScore = 0.0,
                    RowCountScore = 0.0
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Check Vietnamese abbreviation patterns
    /// </summary>
    private FkMatch? CheckVietnameseAbbreviation(ColumnInfo column, EnhancedTableInfo parentTable)
    {
        var columnName = column.ColumnName;

        // Check if column starts with "Ma" (Vietnamese for "Code/ID")
        if (columnName.StartsWith("Ma", StringComparison.OrdinalIgnoreCase))
        {
            var abbreviation = columnName.Substring(2).ToUpper();

            // Check if abbreviation matches parent table
            if (VietnameseAbbreviations.TryGetValue(abbreviation, out var fullName))
            {
                if (parentTable.TableName.Equals(fullName, StringComparison.OrdinalIgnoreCase) ||
                    parentTable.TableName.Contains(fullName, StringComparison.OrdinalIgnoreCase))
                {
                    var pkColumn = parentTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                    if (pkColumn != null)
                    {
                        return new FkMatch
                        {
                            ParentTable = parentTable,
                            ParentColumn = pkColumn,
                            Method = "vietnamese_abbreviation",
                            Reason = $"Column '{columnName}' is Vietnamese abbreviation for '{fullName}' (table: '{parentTable.TableName}')",
                            NamingScore = 0.9,
                            TypeScore = 0.0,
                            RowCountScore = 0.0
                        };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Check if data types are compatible
    /// </summary>
    private bool IsDataTypeCompatible(ColumnInfo childColumn, ColumnInfo parentColumn)
    {
        var childType = NormalizeDataType(childColumn.DataType);
        var parentType = NormalizeDataType(parentColumn.DataType);

        // Exact match
        if (childType == parentType)
            return true;

        // Compatible numeric types
        var numericTypes = new[] { "int", "bigint", "smallint", "tinyint" };
        if (numericTypes.Contains(childType) && numericTypes.Contains(parentType))
            return true;

        // Compatible string types
        var stringTypes = new[] { "varchar", "nvarchar", "char", "nchar" };
        if (stringTypes.Contains(childType) && stringTypes.Contains(parentType))
            return true;

        return false;
    }

    /// <summary>
    /// Check if row count logic is valid (child <= parent)
    /// </summary>
    private bool IsRowCountLogical(EnhancedTableInfo childTable, EnhancedTableInfo parentTable)
    {
        // If no row count data, assume valid
        if (childTable.RowCount == 0 || parentTable.RowCount == 0)
            return true;

        // Child table should have <= rows than parent (with some tolerance for many-to-many)
        // Allow up to 10x more rows in child (for many-to-many scenarios)
        return childTable.RowCount <= parentTable.RowCount * 10;
    }

    /// <summary>
    /// Calculate confidence score for a match
    /// </summary>
    private double CalculateConfidence(FkMatch match)
    {
        // Update type score
        match.TypeScore = 1.0; // Already filtered by compatibility

        // Update row count score
        match.RowCountScore = 1.0; // Already filtered by logic

        // Weighted average
        var weights = new Dictionary<string, double>
        {
            { "naming_pattern", 0.5 },      // 50% weight on naming
            { "name_contains", 0.4 },       // 40% weight on contains
            { "vietnamese_abbreviation", 0.45 }, // 45% weight on abbreviation
        };

        var namingWeight = weights.GetValueOrDefault(match.Method, 0.4);
        var typeWeight = 0.3;
        var rowCountWeight = 0.2;

        var confidence = (match.NamingScore * namingWeight) +
                        (match.TypeScore * typeWeight) +
                        (match.RowCountScore * rowCountWeight);

        return Math.Min(confidence, 1.0);
    }

    /// <summary>
    /// Normalize data type for comparison
    /// </summary>
    private string NormalizeDataType(string dataType)
    {
        return dataType.ToLower()
            .Replace("identity", "")
            .Replace("not null", "")
            .Trim();
    }

    /// <summary>
    /// Check if table is a system table
    /// </summary>
    private bool IsSystemTable(string tableName)
    {
        var systemPrefixes = new[] { "sys", "dbo", "__EFMigrationsHistory", "sysdiagrams" };
        return systemPrefixes.Any(prefix => tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Internal class for FK match candidates
    /// </summary>
    private class FkMatch
    {
        public EnhancedTableInfo ParentTable { get; set; } = null!;
        public ColumnInfo ParentColumn { get; set; } = null!;
        public string Method { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public double NamingScore { get; set; }
        public double TypeScore { get; set; }
        public double RowCountScore { get; set; }
    }
}
