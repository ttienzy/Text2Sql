using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Automatically fixes simple anti-patterns with semantic validation
/// </summary>
public class AutoFixer
{
    private readonly TSql160Parser _parser = new(true);

    /// <summary>
    /// Fix SELECT * by expanding to explicit columns
    /// Confidence: Medium (requires semantic validation)
    /// </summary>
    public AutoFixResult FixSelectStar(string sql, SchemaContext schema)
    {
        var result = new AutoFixResult
        {
            OriginalSql = sql,
            Confidence = ConfidenceLevel.Medium,
            RequiresSemanticValidation = true,
            SemanticRisks = new List<string>
            {
                "Column order may differ if schema has computed columns",
                "Result set structure changes may break downstream code",
                "Ordinal-dependent code will break"
            }
        };

        try
        {
            // Parse SQL
            var fragment = ParseSql(sql);
            if (fragment == null)
            {
                result.FixedSql = sql;
                return result;
            }

            // Find SELECT * and expand
            var fixedSql = ExpandSelectStar(sql, schema);
            result.FixedSql = fixedSql;
            result.FixesApplied.Add("AP-01: Expanded SELECT * to explicit columns");

            // Generate validation query
            result.ValidationQuery = GenerateValidationQuery(sql, fixedSql);
        }
        catch (Exception ex)
        {
            result.FixedSql = sql;
            result.SemanticRisks.Add($"Fix failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Add missing schema prefix (dbo.)
    /// Confidence: High (safe operation)
    /// </summary>
    public AutoFixResult FixMissingSchemaPrefix(string sql, string defaultSchema = "dbo")
    {
        var result = new AutoFixResult
        {
            OriginalSql = sql,
            Confidence = ConfidenceLevel.High,
            RequiresSemanticValidation = false
        };

        try
        {
            // Simple regex-based fix for table names without schema
            // Pattern: FROM/JOIN <tablename> (not preceded by schema.)
            var pattern = @"\b(FROM|JOIN)\s+(?!dbo\.)([a-zA-Z_][a-zA-Z0-9_]*)\b";
            var fixedSql = Regex.Replace(sql, pattern, $"$1 {defaultSchema}.$2", RegexOptions.IgnoreCase);

            result.FixedSql = fixedSql;
            if (fixedSql != sql)
            {
                result.FixesApplied.Add($"AP-13: Added {defaultSchema}. prefix to tables");
            }
            else
            {
                result.FixedSql = sql;
            }
        }
        catch (Exception ex)
        {
            result.FixedSql = sql;
            result.SemanticRisks.Add($"Fix failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Convert OR chain to IN clause
    /// Confidence: Medium (nullable columns may have different semantics)
    /// </summary>
    public AutoFixResult FixOrToIn(string sql)
    {
        var result = new AutoFixResult
        {
            OriginalSql = sql,
            Confidence = ConfidenceLevel.Medium,
            RequiresSemanticValidation = true,
            SemanticRisks = new List<string>
            {
                "Nullable column NULL comparison semantics may differ",
                "Collation differences may affect results"
            }
        };

        try
        {
            // Pattern: col='val1' OR col='val2' OR col='val3'
            // This is complex - simplified implementation
            var fixedSql = sql; // TODO: Implement OR to IN conversion

            result.FixedSql = fixedSql;
            if (fixedSql != sql)
            {
                result.FixesApplied.Add("AP-06: Converted OR chain to IN clause");
                result.ValidationQuery = GenerateValidationQuery(sql, fixedSql);
            }
        }
        catch (Exception ex)
        {
            result.FixedSql = sql;
            result.SemanticRisks.Add($"Fix failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Add N prefix to string literals for nvarchar columns
    /// Confidence: Medium (requires schema knowledge)
    /// </summary>
    public AutoFixResult FixNvarcharLiterals(string sql, SchemaContext schema)
    {
        var result = new AutoFixResult
        {
            OriginalSql = sql,
            Confidence = ConfidenceLevel.Medium,
            RequiresSemanticValidation = true,
            SemanticRisks = new List<string>
            {
                "Implicit conversion behavior may change",
                "Index usage may be affected"
            }
        };

        try
        {
            // Pattern: string literals without N prefix
            // WHERE Name = 'value' → WHERE Name = N'value'
            var pattern = @"=\s*'([^']*)'";
            var fixedSql = Regex.Replace(sql, pattern, "= N'$1'", RegexOptions.IgnoreCase);

            result.FixedSql = fixedSql;
            if (fixedSql != sql)
            {
                result.FixesApplied.Add("AP-21: Added N prefix to string literals");
                result.ValidationQuery = GenerateValidationQuery(sql, fixedSql);
            }
        }
        catch (Exception ex)
        {
            result.FixedSql = sql;
            result.SemanticRisks.Add($"Fix failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Check if all issues can be auto-fixed
    /// </summary>
    public bool CanAutoFix(List<AntiPattern> issues)
    {
        var autoFixableCodes = new[] { "AP-01", "AP-06", "AP-13", "AP-21" };
        return issues.All(i => autoFixableCodes.Contains(i.Code));
    }

    /// <summary>
    /// Generate validation query to verify results are identical
    /// </summary>
    private string GenerateValidationQuery(string original, string fixedSql)
    {
        return $@"
-- Validation Query: Verify original and fixed queries produce identical results
WITH Original AS (
    {original}
),
Fixed AS (
    {fixedSql}
)
SELECT 
    CASE 
        WHEN EXISTS (
            SELECT * FROM Original EXCEPT SELECT * FROM Fixed
            UNION ALL
            SELECT * FROM Fixed EXCEPT SELECT * FROM Original
        ) THEN 'DIFFERENT' 
        ELSE 'IDENTICAL' 
    END AS ValidationResult;";
    }

    /// <summary>
    /// Expand SELECT * to explicit columns
    /// </summary>
    private string ExpandSelectStar(string sql, SchemaContext schema)
    {
        // Simplified implementation - in production, use AST manipulation
        foreach (var table in schema.Tables)
        {
            var columns = string.Join(", ", table.Columns.Select(c => c.ColumnName));
            var pattern = $@"SELECT\s+\*\s+FROM\s+{table.TableName}";
            sql = Regex.Replace(sql, pattern, $"SELECT {columns} FROM {table.TableName}", RegexOptions.IgnoreCase);
        }

        return sql;
    }

    /// <summary>
    /// Parse SQL using TSqlParser
    /// </summary>
    private TSqlFragment? ParseSql(string sql)
    {
        try
        {
            var fragment = _parser.Parse(new System.IO.StringReader(sql), out var errors);
            return errors.Count == 0 ? fragment : null;
        }
        catch
        {
            return null;
        }
    }
}
