namespace TextToSqlAgent.Core.Models;

public class SchemaSemanticProfile
{
    public string ConnectionId { get; set; } = string.Empty;
    public string? DatabaseDescription { get; set; }
    public List<string> GlobalSynonyms { get; set; } = new();
    public List<TableSemanticProfile> Tables { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }

    public TableSemanticProfile? FindTable(string tableName)
    {
        return Tables.FirstOrDefault(t =>
            t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
    }
}

public class TableSemanticProfile
{
    public string TableName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BusinessMeaning { get; set; }
    public List<string> Synonyms { get; set; } = new();
    public List<ColumnSemanticOverride> Columns { get; set; } = new();

    public ColumnSemanticOverride? FindColumn(string columnName)
    {
        return Columns.FirstOrDefault(c =>
            c.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
    }
}

public class ColumnSemanticOverride
{
    public string ColumnName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BusinessMeaning { get; set; }
    public string? Role { get; set; }
    public string? DisplayPriority { get; set; }
    public bool? PreferredForReports { get; set; }
    public List<string> Synonyms { get; set; } = new();
}

public static class SchemaSemanticProfileApplier
{
    public static DatabaseSchema Apply(DatabaseSchema schema, SchemaSemanticProfile? profile)
    {
        if (profile == null)
        {
            return schema;
        }

        foreach (var table in schema.Tables)
        {
            var tableProfile = profile.FindTable(table.TableName);
            if (tableProfile == null)
            {
                continue;
            }

            table.Description = FirstNonEmpty(tableProfile.BusinessMeaning, tableProfile.Description, table.Description);

            foreach (var column in table.Columns)
            {
                var columnProfile = tableProfile.FindColumn(column.ColumnName);
                if (columnProfile == null)
                {
                    continue;
                }

                column.Description = FirstNonEmpty(columnProfile.BusinessMeaning, columnProfile.Description, column.Description);
                column.Role = FirstNonEmpty(columnProfile.Role, column.Role);
                column.DisplayPriority = FirstNonEmpty(columnProfile.DisplayPriority, column.DisplayPriority);

                if (columnProfile.PreferredForReports.HasValue)
                {
                    column.PreferredForReports = columnProfile.PreferredForReports.Value;
                }
            }
        }

        return schema;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}
