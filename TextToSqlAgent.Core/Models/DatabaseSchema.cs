namespace TextToSqlAgent.Core.Models;

public class DatabaseSchema
{
    public List<TableInfo> Tables { get; set; } = new();
    public List<RelationshipInfo> Relationships { get; set; } = new();
    public DateTime ScannedAt { get; set; }
}

public class TableInfo
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string? Description { get; set; }
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<string> PrimaryKeys { get; set; } = new();
}

public class ColumnInfo
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Role { get; set; }
    public string? DisplayPriority { get; set; }
    public bool PreferredForReports { get; set; }
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public string? DefaultValue { get; set; }
}

public class RelationshipInfo
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
}

public class ColumnSemanticProfile
{
    public string Role { get; set; } = "attribute";
    public string DisplayPriority { get; set; } = "medium";
    public bool PreferredForReports { get; set; }
    public bool IsTechnical { get; set; }
}

public static class ColumnSemanticHints
{
    private static readonly string[] DisplayLabelTokens =
    [
        "name",
        "title",
        "label",
        "fullname",
        "displayname"
    ];

    private static readonly string[] TimeDimensionTokens =
    [
        "date",
        "time",
        "month",
        "year",
        "quarter",
        "week",
        "day"
    ];

    private static readonly string[] MetricTokens =
    [
        "amount",
        "revenue",
        "total",
        "price",
        "cost",
        "quantity",
        "count",
        "score",
        "rating",
        "percent",
        "percentage"
    ];

    private static readonly string[] AuditTokens =
    [
        "createdat",
        "createddate",
        "createdon",
        "updatedat",
        "updateddate",
        "modifiedat",
        "modifieddate",
        "deletedat",
        "deleteddate",
        "timestamp",
        "rowversion"
    ];

    public static ColumnSemanticProfile Infer(ColumnInfo column, string? tableName = null)
    {
        if (!string.IsNullOrWhiteSpace(column.Role) ||
            !string.IsNullOrWhiteSpace(column.DisplayPriority) ||
            column.PreferredForReports)
        {
            var role = string.IsNullOrWhiteSpace(column.Role) ? "attribute" : column.Role!;
            var displayPriority = string.IsNullOrWhiteSpace(column.DisplayPriority) ? "medium" : column.DisplayPriority!;
            return new ColumnSemanticProfile
            {
                Role = role,
                DisplayPriority = displayPriority,
                PreferredForReports = column.PreferredForReports ||
                                      string.Equals(role, "display_label", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(displayPriority, "high", StringComparison.OrdinalIgnoreCase),
                IsTechnical = IsTechnicalRole(role)
            };
        }

        var normalized = Normalize(column.ColumnName);

        if (IsAuditColumn(normalized))
        {
            return new ColumnSemanticProfile
            {
                Role = "audit_field",
                DisplayPriority = "low",
                PreferredForReports = false,
                IsTechnical = true
            };
        }

        if (column.IsPrimaryKey || column.IsForeignKey || IsIdColumn(normalized, tableName))
        {
            return new ColumnSemanticProfile
            {
                Role = "technical_key",
                DisplayPriority = "low",
                PreferredForReports = false,
                IsTechnical = true
            };
        }

        if (ContainsAny(normalized, DisplayLabelTokens))
        {
            return new ColumnSemanticProfile
            {
                Role = "display_label",
                DisplayPriority = "high",
                PreferredForReports = true,
                IsTechnical = false
            };
        }

        if (ContainsAny(normalized, TimeDimensionTokens))
        {
            return new ColumnSemanticProfile
            {
                Role = "time_dimension",
                DisplayPriority = "high",
                PreferredForReports = true,
                IsTechnical = false
            };
        }

        if (ContainsAny(normalized, MetricTokens))
        {
            return new ColumnSemanticProfile
            {
                Role = "business_metric",
                DisplayPriority = "high",
                PreferredForReports = true,
                IsTechnical = false
            };
        }

        if (normalized.StartsWith("is") || normalized.Contains("flag"))
        {
            return new ColumnSemanticProfile
            {
                Role = "internal_flag",
                DisplayPriority = "low",
                PreferredForReports = false,
                IsTechnical = true
            };
        }

        return new ColumnSemanticProfile
        {
            Role = "attribute",
            DisplayPriority = "medium",
            PreferredForReports = false,
            IsTechnical = false
        };
    }

    private static bool IsTechnicalRole(string? role)
    {
        return string.Equals(role, "technical_key", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "surrogate_key", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "foreign_key", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "audit_field", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "internal_flag", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuditColumn(string normalizedName)
    {
        return AuditTokens.Any(token => normalizedName.Contains(token));
    }

    private static bool IsIdColumn(string normalizedName, string? tableName)
    {
        if (normalizedName == "id" || normalizedName.EndsWith("id"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return false;
        }

        var normalizedTable = Normalize(tableName).TrimEnd('s');
        return normalizedName == $"{normalizedTable}id";
    }

    private static bool ContainsAny(string normalizedName, IEnumerable<string> tokens)
    {
        return tokens.Any(normalizedName.Contains);
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
