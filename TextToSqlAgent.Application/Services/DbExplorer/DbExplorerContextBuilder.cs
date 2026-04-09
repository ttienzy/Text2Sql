using TextToSqlAgent.Core.Models;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Builds rich context for Chat integration from DB Explorer
/// </summary>
public class DbExplorerContextBuilder
{
    /// <summary>
    /// Build context for table query
    /// </summary>
    public static TableContext BuildTableContext(
        EnhancedTableInfo table,
        List<RelationshipInfo>? relationships = null,
        DatabaseAnalysis? analysis = null)
    {
        var context = new TableContext
        {
            Source = "db-explorer",
            TableName = table.TableName,
            Schema = table.Schema,
            RowCount = table.RowCount,
            ColumnCount = table.ColumnCount,
            Role = table.Role?.ToString(),
            Module = table.Module
        };

        // Add columns
        context.Columns = table.Columns.Select(c => new ColumnContext
        {
            Name = c.ColumnName,
            DataType = c.DataType,
            IsNullable = c.IsNullable,
            IsPrimaryKey = c.IsPrimaryKey,
            IsForeignKey = c.IsForeignKey
        }).ToList();

        // Add relationships
        if (relationships != null)
        {
            context.Relationships = relationships
                .Where(r => r.FromTable == table.TableName || r.ToTable == table.TableName)
                .Select(r => new RelationshipContext
                {
                    FromTable = r.FromTable,
                    FromColumn = r.FromColumn,
                    ToTable = r.ToTable,
                    ToColumn = r.ToColumn,
                    Type = r.FromTable == table.TableName ? "outgoing" : "incoming"
                }).ToList();
        }

        // Add description from analysis
        if (analysis != null && analysis.TableRoles.TryGetValue(table.TableName, out var roleInfo))
        {
            context.Description = roleInfo.Description;
        }

        // Generate suggested questions
        context.SuggestedQuestions = GenerateSuggestedQuestions(table, context.Relationships);

        return context;
    }

    /// <summary>
    /// Generate suggested questions based on table context
    /// </summary>
    private static List<string> GenerateSuggestedQuestions(
        EnhancedTableInfo table,
        List<RelationshipContext> relationships)
    {
        var questions = new List<string>();
        var tableName = table.TableName;

        // Basic queries
        questions.Add($"Hiển thị tất cả dữ liệu từ bảng {tableName}");
        questions.Add($"Show top 10 rows from {tableName}");

        // Count queries
        questions.Add($"Đếm số lượng records trong {tableName}");

        // Date-based queries (if has date columns)
        var dateColumns = table.Columns
            .Where(c => c.DataType.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                       c.DataType.Contains("time", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (dateColumns.Any())
        {
            var dateCol = dateColumns.First().ColumnName;
            questions.Add($"Phân tích {tableName} theo {dateCol}");
            questions.Add($"Show {tableName} records from last 30 days");
        }

        // Relationship-based queries
        if (relationships.Any())
        {
            var outgoing = relationships.Where(r => r.Type == "outgoing").ToList();
            var incoming = relationships.Where(r => r.Type == "incoming").ToList();

            if (outgoing.Any())
            {
                var rel = outgoing.First();
                questions.Add($"Hiển thị {tableName} với thông tin từ {rel.ToTable}");
            }

            if (incoming.Any())
            {
                var rel = incoming.First();
                questions.Add($"Tìm {tableName} có liên quan đến {rel.FromTable}");
            }
        }

        // Aggregation queries (if has numeric columns)
        var numericColumns = table.Columns
            .Where(c => c.DataType.Contains("int", StringComparison.OrdinalIgnoreCase) ||
                       c.DataType.Contains("decimal", StringComparison.OrdinalIgnoreCase) ||
                       c.DataType.Contains("float", StringComparison.OrdinalIgnoreCase) ||
                       c.DataType.Contains("money", StringComparison.OrdinalIgnoreCase))
            .Where(c => !c.IsPrimaryKey && !c.IsForeignKey)
            .ToList();

        if (numericColumns.Any())
        {
            var numCol = numericColumns.First().ColumnName;
            questions.Add($"Tính tổng {numCol} trong {tableName}");
            questions.Add($"Calculate average {numCol} by group");
        }

        // Status/category queries (if has status-like columns)
        var statusColumns = table.Columns
            .Where(c => c.ColumnName.Contains("status", StringComparison.OrdinalIgnoreCase) ||
                       c.ColumnName.Contains("state", StringComparison.OrdinalIgnoreCase) ||
                       c.ColumnName.Contains("type", StringComparison.OrdinalIgnoreCase) ||
                       c.ColumnName.Contains("category", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (statusColumns.Any())
        {
            var statusCol = statusColumns.First().ColumnName;
            questions.Add($"Phân tích {tableName} theo {statusCol}");
        }

        // Data quality queries
        questions.Add($"Kiểm tra data quality của {tableName}");
        questions.Add($"Find duplicate records in {tableName}");

        // Limit to 8 most relevant questions
        return questions.Take(8).ToList();
    }

    /// <summary>
    /// Build context message for Chat
    /// </summary>
    public static string BuildContextMessage(
        TableContext context,
        string contextType = "query")
    {
        var tableName = context.TableName;
        var description = !string.IsNullOrEmpty(context.Description)
            ? $" {context.Description}"
            : "";

        switch (contextType)
        {
            case "query":
                return $"I want to query the {tableName} table.{description}\n\n" +
                       $"Table info:\n" +
                       $"- Rows: {context.RowCount:N0}\n" +
                       $"- Columns: {context.ColumnCount}\n" +
                       $"- Role: {context.Role ?? "Unknown"}\n" +
                       $"- Module: {context.Module ?? "Unknown"}";

            case "relationships":
                var relCount = context.Relationships.Count;
                var relTables = string.Join(", ", context.Relationships.Select(r =>
                    r.Type == "outgoing" ? r.ToTable : r.FromTable).Distinct());

                return $"Explain the relationships of {tableName} table.{description}\n\n" +
                       $"It has {relCount} relationships with: {relTables}\n\n" +
                       $"Relationships:\n" +
                       string.Join("\n", context.Relationships.Select(r =>
                           $"- {r.FromTable}.{r.FromColumn} → {r.ToTable}.{r.ToColumn}"));

            case "quality":
                var nullableCols = context.Columns.Count(c => c.IsNullable);
                var pkCount = context.Columns.Count(c => c.IsPrimaryKey);
                var fkCount = context.Columns.Count(c => c.IsForeignKey);

                return $"Analyze data quality issues in {tableName} table.{description}\n\n" +
                       $"Table structure:\n" +
                       $"- Total columns: {context.ColumnCount}\n" +
                       $"- Nullable columns: {nullableCols}\n" +
                       $"- Primary keys: {pkCount}\n" +
                       $"- Foreign keys: {fkCount}\n\n" +
                       $"Please check for:\n" +
                       $"- Missing indexes on FK columns\n" +
                       $"- High null rates\n" +
                       $"- Data integrity issues\n" +
                       $"- Duplicate records";

            case "analyze":
                return $"Provide a comprehensive analysis of {tableName} table.{description}\n\n" +
                       $"Include:\n" +
                       $"- Data distribution patterns\n" +
                       $"- Common queries for this table\n" +
                       $"- Performance optimization suggestions\n" +
                       $"- Business insights from the data";

            default:
                return $"I want to query the {tableName} table.{description}";
        }
    }
}

/// <summary>
/// Table context for Chat integration
/// </summary>
public class TableContext
{
    public string Source { get; set; } = "db-explorer";
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string? Role { get; set; }
    public string? Module { get; set; }
    public string? Description { get; set; }
    public List<ColumnContext> Columns { get; set; } = new();
    public List<RelationshipContext> Relationships { get; set; } = new();
    public List<string> SuggestedQuestions { get; set; } = new();
}

/// <summary>
/// Column context
/// </summary>
public class ColumnContext
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
}

/// <summary>
/// Relationship context
/// </summary>
public class RelationshipContext
{
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
    public string Type { get; set; } = "outgoing"; // "outgoing" or "incoming"
}
