using System.Text;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Generates living documentation from database schema analysis
/// Supports Markdown export format
/// </summary>
public class DocumentationGenerator
{
    private readonly ILogger<DocumentationGenerator> _logger;

    public DocumentationGenerator(ILogger<DocumentationGenerator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generate Markdown documentation
    /// </summary>
    public string GenerateMarkdown(
        EnhancedDatabaseSchema schema,
        DatabaseAnalysis? analysis = null,
        string? databaseName = null,
        string? serverName = null)
    {
        var dbName = databaseName ?? "Unknown Database";
        var server = serverName ?? "Unknown Server";

        _logger.LogInformation("[DocumentationGenerator] Generating Markdown for {Database}", dbName);

        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# Database Documentation: {dbName}");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Server:** {server}");
        sb.AppendLine($"**Total Tables:** {schema.EnhancedTables.Count}");
        sb.AppendLine();

        // Overview section
        if (analysis != null)
        {
            sb.AppendLine("## 📊 Overview");
            sb.AppendLine();
            sb.AppendLine($"**Domain:** {analysis.Domain}");
            sb.AppendLine();
            sb.AppendLine($"**Summary:** {analysis.Summary}");
            sb.AppendLine();

            // Key tables
            if (analysis.KeyTables?.Any() == true)
            {
                sb.AppendLine("**Key Tables:**");
                foreach (var keyTable in analysis.KeyTables)
                {
                    sb.AppendLine($"- 🔑 {keyTable}");
                }
                sb.AppendLine();
            }

            // Data flow pattern
            if (!string.IsNullOrEmpty(analysis.DataFlowPattern))
            {
                sb.AppendLine($"**Data Flow:** {analysis.DataFlowPattern}");
                sb.AppendLine();
            }

            // Technical debt
            if (analysis.TechnicalDebt?.Any() == true)
            {
                sb.AppendLine("**⚠️ Technical Debt:**");
                foreach (var debt in analysis.TechnicalDebt)
                {
                    sb.AppendLine($"- {debt}");
                }
                sb.AppendLine();
            }

            // Modules
            if (analysis.Modules?.Any() == true)
            {
                sb.AppendLine("## 📦 Modules");
                sb.AppendLine();
                foreach (var module in analysis.Modules)
                {
                    sb.AppendLine($"### {module.Name}");
                    sb.AppendLine();
                    sb.AppendLine(module.Description);
                    sb.AppendLine();
                    sb.AppendLine($"**Tables:** {string.Join(", ", module.Tables)}");
                    sb.AppendLine();
                }
            }
        }

        // Tables section
        sb.AppendLine("## 📋 Tables");
        sb.AppendLine();

        // Group by module if available
        var groupedTables = schema.EnhancedTables
            .GroupBy(t => t.Module ?? "Uncategorized")
            .OrderBy(g => g.Key);

        foreach (var group in groupedTables)
        {
            sb.AppendLine($"### Module: {group.Key}");
            sb.AppendLine();

            foreach (var table in group.OrderBy(t => t.TableName))
            {
                GenerateTableSection(sb, table, analysis);
            }
        }

        // Health issues section
        if (analysis?.HealthIssues?.Any() == true)
        {
            sb.AppendLine("## 🏥 Health Issues");
            sb.AppendLine();

            var criticalIssues = analysis.HealthIssues
                .Where(i => i.Severity == IssueSeverity.Critical)
                .ToList();
            var warningIssues = analysis.HealthIssues
                .Where(i => i.Severity == IssueSeverity.Warning)
                .ToList();
            var infoIssues = analysis.HealthIssues
                .Where(i => i.Severity == IssueSeverity.Info)
                .ToList();

            if (criticalIssues.Any())
            {
                sb.AppendLine("### 🔴 Critical Issues");
                sb.AppendLine();
                foreach (var issue in criticalIssues)
                {
                    GenerateIssueSection(sb, issue);
                }
            }

            if (warningIssues.Any())
            {
                sb.AppendLine("### 🟡 Warnings");
                sb.AppendLine();
                foreach (var issue in warningIssues)
                {
                    GenerateIssueSection(sb, issue);
                }
            }

            if (infoIssues.Any())
            {
                sb.AppendLine("### 🔵 Information");
                sb.AppendLine();
                foreach (var issue in infoIssues)
                {
                    GenerateIssueSection(sb, issue);
                }
            }
        }

        // Relationships section
        if (schema.BaseSchema.Relationships?.Any() == true)
        {
            sb.AppendLine("## 🔗 Relationships");
            sb.AppendLine();
            sb.AppendLine("| From Table | From Column | To Table | To Column |");
            sb.AppendLine("|------------|-------------|----------|-----------|");

            foreach (var rel in schema.BaseSchema.Relationships.OrderBy(r => r.FromTable))
            {
                sb.AppendLine($"| {rel.FromTable} | {rel.FromColumn} | {rel.ToTable} | {rel.ToColumn} |");
            }
            sb.AppendLine();
        }

        // Footer
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Generated by AI DB Explorer on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");

        return sb.ToString();
    }

    /// <summary>
    /// Generate table section in Markdown
    /// </summary>
    private void GenerateTableSection(
        StringBuilder sb,
        EnhancedTableInfo table,
        DatabaseAnalysis? analysis)
    {
        sb.AppendLine($"#### {table.TableName}");
        sb.AppendLine();

        // Table metadata
        sb.AppendLine($"**Schema:** {table.Schema}");
        sb.AppendLine($"**Rows:** {table.RowCount:N0}");
        sb.AppendLine($"**Columns:** {table.ColumnCount}");
        sb.AppendLine($"**Role:** {table.Role?.ToString() ?? "Unknown"}");
        sb.AppendLine();

        // Description from analysis
        if (analysis?.TableRoles.TryGetValue(table.TableName, out var roleInfo) == true)
        {
            sb.AppendLine($"**Purpose:** {roleInfo.Description}");
            sb.AppendLine();
        }

        // Columns table
        sb.AppendLine("**Columns:**");
        sb.AppendLine();
        sb.AppendLine("| Column | Type | Nullable | Keys | Description |");
        sb.AppendLine("|--------|------|----------|------|-------------|");

        foreach (var col in table.Columns)
        {
            var keys = new List<string>();
            if (col.IsPrimaryKey) keys.Add("PK");
            if (col.IsForeignKey) keys.Add("FK");
            var keysStr = keys.Any() ? string.Join(", ", keys) : "-";

            var nullable = col.IsNullable ? "Yes" : "No";

            sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {nullable} | {keysStr} | - |");
        }
        sb.AppendLine();

        // Indexes
        if (table.Indexes?.Any() == true)
        {
            sb.AppendLine("**Indexes:**");
            foreach (var idx in table.Indexes)
            {
                var unique = idx.IsUnique ? "UNIQUE" : "";
                var pk = idx.IsPrimaryKey ? "PRIMARY KEY" : "INDEX";
                sb.AppendLine($"- {idx.IndexName} ({unique} {pk})".Trim());
            }
            sb.AppendLine();
        }

        // Foreign keys (from ForeignKeys list)
        if (table.ForeignKeys?.Any() == true)
        {
            sb.AppendLine("**Foreign Keys:**");
            foreach (var fkCol in table.ForeignKeys)
            {
                sb.AppendLine($"- {fkCol}");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Generate health issue section
    /// </summary>
    private void GenerateIssueSection(StringBuilder sb, HealthIssue issue)
    {
        var icon = issue.Severity switch
        {
            IssueSeverity.Critical => "🔴",
            IssueSeverity.Warning => "🟡",
            _ => "🔵"
        };

        sb.AppendLine($"#### {icon} {issue.Type}");
        sb.AppendLine();
        sb.AppendLine($"**Table:** {issue.Table}");
        if (!string.IsNullOrEmpty(issue.Column))
        {
            sb.AppendLine($"**Column:** {issue.Column}");
        }
        sb.AppendLine();
        sb.AppendLine($"**Description:** {issue.Description}");
        sb.AppendLine();
        sb.AppendLine($"**Recommendation:** {issue.Recommendation}");
        sb.AppendLine();
    }

    /// <summary>
    /// Generate documentation summary (lightweight version)
    /// </summary>
    public DocumentationSummary GenerateSummary(
        EnhancedDatabaseSchema schema,
        DatabaseAnalysis? analysis = null,
        string? databaseName = null,
        string? serverName = null)
    {
        return new DocumentationSummary
        {
            DatabaseName = databaseName ?? "Unknown Database",
            ServerName = serverName ?? "Unknown Server",
            GeneratedAt = DateTime.UtcNow,
            TableCount = schema.EnhancedTables.Count,
            RelationshipCount = schema.BaseSchema.Relationships.Count,
            Domain = analysis?.Domain ?? "Unknown",
            ModuleCount = analysis?.Modules?.Count ?? 0,
            CriticalIssues = analysis?.HealthIssues?.Count(i => i.Severity == IssueSeverity.Critical) ?? 0,
            WarningIssues = analysis?.HealthIssues?.Count(i => i.Severity == IssueSeverity.Warning) ?? 0,
            InfoIssues = analysis?.HealthIssues?.Count(i => i.Severity == IssueSeverity.Info) ?? 0
        };
    }
}

/// <summary>
/// Documentation summary metadata
/// </summary>
public class DocumentationSummary
{
    public string DatabaseName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TableCount { get; set; }
    public int RelationshipCount { get; set; }
    public string Domain { get; set; } = "Unknown";
    public int ModuleCount { get; set; }
    public int CriticalIssues { get; set; }
    public int WarningIssues { get; set; }
    public int InfoIssues { get; set; }
}
