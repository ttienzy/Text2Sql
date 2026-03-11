using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Core.Models;

namespace TextToSqlAgent.Infrastructure.Validation;

public class SchemaValidator
{
    private readonly DatabaseSchema _schema;
    private readonly ILogger<SchemaValidator>? _logger;

    public SchemaValidator(DatabaseSchema schema, ILogger<SchemaValidator>? logger = null)
    {
        _schema = schema;
        _logger = logger;
    }

    public ValidationResult Validate(string sql)
    {
        var result = new ValidationResult();

        try
        {
            // Extract tables from SQL
            var tables = ExtractTables(sql);
            foreach (var table in tables)
            {
                if (!_schema.Tables.Any(t => t.TableName.Equals(table, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add($"Table '{table}' not found in schema");
                }
            }

            // Extract columns from SQL
            var columns = ExtractColumns(sql);
            foreach (var (table, column) in columns)
            {
                var schemaTable = _schema.Tables.FirstOrDefault(t =>
                    t.TableName.Equals(table, StringComparison.OrdinalIgnoreCase));

                if (schemaTable != null)
                {
                    if (!schemaTable.Columns.Any(c =>
                        c.ColumnName.Equals(column, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Errors.Add($"Column '{column}' not found in table '{table}'");
                    }
                }
            }

            result.IsValid = !result.Errors.Any();

            if (result.IsValid)
            {
                _logger?.LogDebug("[SchemaValidator] Validation passed");
            }
            else
            {
                _logger?.LogWarning("[SchemaValidator] Validation failed: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SchemaValidator] Error during validation");
            result.Errors.Add($"Validation error: {ex.Message}");
            result.IsValid = false;
        }

        return result;
    }

    private List<string> ExtractTables(string sql)
    {
        var tables = new List<string>();

        // Pattern: FROM/JOIN [schema].[table] or FROM/JOIN [table]
        var pattern = @"(?:FROM|JOIN)\s+(?:\[?[\w]+\]?\.)?\[?(\w+)\]?";
        var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                tables.Add(match.Groups[1].Value);
            }
        }

        return tables.Distinct().ToList();
    }

    private List<(string Table, string Column)> ExtractColumns(string sql)
    {
        var columns = new List<(string, string)>();

        // Pattern: [table].[column] or [t].[column] (alias)
        var pattern = @"\[?(\w+)\]?\.\[?(\w+)\]?";
        var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 2)
            {
                var table = match.Groups[1].Value;
                var column = match.Groups[2].Value;

                // Try to resolve alias to actual table name
                var actualTable = ResolveTableAlias(sql, table);
                columns.Add((actualTable ?? table, column));
            }
        }

        return columns.Distinct().ToList();
    }

    private string? ResolveTableAlias(string sql, string alias)
    {
        // Pattern: FROM/JOIN [table] AS [alias]
        var pattern = $@"(?:FROM|JOIN)\s+\[?(\w+)\]?\s+(?:AS\s+)?\[?{alias}\]?";
        var match = Regex.Match(sql, pattern, RegexOptions.IgnoreCase);

        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
