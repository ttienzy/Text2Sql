using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;
using TextToSqlAgent.Application.Options;
using TextToSqlAgent.Core.Models.DbExplorer;

namespace TextToSqlAgent.Application.Services.DbExplorer;

/// <summary>
/// Rule engine for executing health check rules from JSON files
/// </summary>
public class RuleEngine
{
    private readonly ILogger<RuleEngine> _logger;
    private readonly DbExplorerOptions _options;
    private readonly List<HealthCheckRule> _rules = new();

    public RuleEngine(
        ILogger<RuleEngine> logger,
        IOptions<DbExplorerOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Load all rules from JSON files
    /// </summary>
    public async Task LoadRulesAsync()
    {
        _rules.Clear();

        var ruleFiles = new[]
        {
            "HealthCheckRules/critical-rules.json",
            "HealthCheckRules/warning-rules.json",
            "HealthCheckRules/info-rules.json"
        };

        foreach (var file in ruleFiles)
        {
            if (!File.Exists(file))
            {
                _logger.LogWarning("[RuleEngine] Rule file not found: {File}", file);
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(file);
                var ruleSet = JsonSerializer.Deserialize<HealthCheckRuleSet>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ruleSet?.Rules != null)
                {
                    _rules.AddRange(ruleSet.Rules);
                    _logger.LogInformation("[RuleEngine] Loaded {Count} rules from {File}",
                        ruleSet.Rules.Count, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RuleEngine] Failed to load rules from {File}", file);
            }
        }

        _logger.LogInformation("[RuleEngine] Total rules loaded: {Count}", _rules.Count);
    }

    /// <summary>
    /// Execute rules against schema
    /// </summary>
    public List<HealthIssue> ExecuteRules(EnhancedDatabaseSchema schema)
    {
        var issues = new List<HealthIssue>();

        foreach (var rule in _rules)
        {
            try
            {
                var ruleIssues = ExecuteRule(rule, schema);
                issues.AddRange(ruleIssues);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RuleEngine] Failed to execute rule: {RuleId}", rule.Id);
            }
        }

        _logger.LogInformation("[RuleEngine] Found {Count} issues", issues.Count);
        return issues;
    }

    private List<HealthIssue> ExecuteRule(HealthCheckRule rule, EnhancedDatabaseSchema schema)
    {
        var issues = new List<HealthIssue>();

        foreach (var table in schema.EnhancedTables)
        {
            // Skip ignored tables
            if (!string.IsNullOrEmpty(_options.HealthCheck.IgnoreTablesRegex))
            {
                if (Regex.IsMatch(table.TableName, _options.HealthCheck.IgnoreTablesRegex))
                {
                    continue;
                }
            }

            // Execute table-level rules
            if (rule.Check.Condition.Contains("table."))
            {
                if (EvaluateTableCondition(rule, table, schema))
                {
                    issues.Add(CreateIssue(rule, table, null));
                }
            }

            // Execute column-level rules
            if (rule.Check.Condition.Contains("column."))
            {
                foreach (var column in table.Columns)
                {
                    if (EvaluateColumnCondition(rule, table, column))
                    {
                        issues.Add(CreateIssue(rule, table, column));
                    }
                }
            }
        }

        return issues;
    }

    private bool EvaluateTableCondition(HealthCheckRule rule, EnhancedTableInfo table, EnhancedDatabaseSchema schema)
    {
        var condition = rule.Check.Condition;

        // Missing PK
        if (condition.Contains("table.PrimaryKeys.Count == 0"))
        {
            return table.PrimaryKeys.Count == 0;
        }

        // Too many columns
        if (condition.Contains("table.ColumnCount > config.MaxColumnsPerTable"))
        {
            return table.ColumnCount > _options.HealthCheck.MaxColumnsPerTable;
        }

        // Orphan table
        if (condition.Contains("!schema.Relationships.Any"))
        {
            return !schema.BaseSchema.Relationships.Any(r =>
                r.FromTable == table.TableName || r.ToTable == table.TableName);
        }

        // Missing audit columns
        if (condition.Contains("!table.Columns.Any(c => auditColumns.Contains(c.Name))"))
        {
            return !table.Columns.Any(c => _options.HealthCheck.AuditColumnNames.Contains(c.ColumnName));
        }

        return false;
    }

    private bool EvaluateColumnCondition(HealthCheckRule rule, EnhancedTableInfo table, Core.Models.ColumnInfo column)
    {
        var condition = rule.Check.Condition;

        // Password column not encrypted
        if (condition.Contains("column.Name matches passwordPattern"))
        {
            var isPasswordColumn = _options.HealthCheck.PasswordColumnPatterns
                .Any(pattern => column.ColumnName.ToLower().Contains(pattern.ToLower()));
            var isVarchar = column.DataType.ToLower().Contains("varchar") ||
                           column.DataType.ToLower().Contains("nvarchar");
            return isPasswordColumn && isVarchar;
        }

        // FK without index
        if (condition.Contains("column.IsForeignKey AND !table.Indexes.Any"))
        {
            if (!column.IsForeignKey) return false;
            return !table.Indexes.Any(i => i.Columns.Contains(column.ColumnName));
        }

        // Nullable FK
        if (condition.Contains("column.IsForeignKey AND column.IsNullable"))
        {
            return column.IsForeignKey && column.IsNullable;
        }

        // VARCHAR(MAX) without length
        if (condition.Contains("column.DataType == 'varchar' AND column.MaxLength == -1"))
        {
            return column.DataType.ToLower().Contains("varchar") && column.MaxLength == -1;
        }

        return false;
    }

    private HealthIssue CreateIssue(HealthCheckRule rule, EnhancedTableInfo table, Core.Models.ColumnInfo? column)
    {
        var message = rule.Message
            .Replace("{tableName}", table.TableName)
            .Replace("{columnName}", column?.ColumnName ?? "")
            .Replace("{columnCount}", table.ColumnCount.ToString())
            .Replace("{threshold}", _options.HealthCheck.MaxColumnsPerTable.ToString());

        var recommendation = rule.Recommendation
            .Replace("{tableName}", table.TableName)
            .Replace("{columnName}", column?.ColumnName ?? "");

        var sqlFix = rule.SqlFix?
            .Replace("{tableName}", table.TableName)
            .Replace("{schema}", table.Schema)
            .Replace("{columnName}", column?.ColumnName ?? "")
            .Replace("{dataType}", column?.DataType ?? "");

        return new HealthIssue
        {
            Severity = ParseSeverity(rule.Severity),
            Type = ParseIssueType(rule.Id),
            Table = table.TableName,
            Column = column?.ColumnName,
            Description = message,
            Recommendation = sqlFix ?? recommendation
        };
    }

    private IssueSeverity ParseSeverity(string severity)
    {
        return severity.ToLower() switch
        {
            "critical" => IssueSeverity.Critical,
            "warning" => IssueSeverity.Warning,
            _ => IssueSeverity.Info
        };
    }

    private IssueType ParseIssueType(string ruleId)
    {
        return ruleId switch
        {
            "missing-pk" => IssueType.MissingPrimaryKey,
            "missing-fk-index" => IssueType.MissingIndex,
            "orphan-table" => IssueType.OrphanTable,
            "inconsistent-naming" => IssueType.InconsistentNaming,
            "nullable-fk" => IssueType.NullableRequired,
            _ => IssueType.UnusedTable
        };
    }
}

/// <summary>
/// Health check rule set from JSON
/// </summary>
public class HealthCheckRuleSet
{
    public List<HealthCheckRule> Rules { get; set; } = new();
}

/// <summary>
/// Health check rule definition
/// </summary>
public class HealthCheckRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Type { get; set; } = "metadata";
    public RuleCheck Check { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string? SqlFix { get; set; }
    public string? Documentation { get; set; }
    public string? EstimatedImpact { get; set; }
}

/// <summary>
/// Rule check condition
/// </summary>
public class RuleCheck
{
    public string Condition { get; set; } = string.Empty;
}
