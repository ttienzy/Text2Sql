using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TextToSqlAgent.Application.Services.QueryOptimizer.Models;

namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// AST visitor to extract metadata and detect anti-patterns from SQL query
/// Uses TSqlFragmentVisitor pattern to traverse ScriptDom AST
/// </summary>
public class QueryMetadataVisitor : TSqlFragmentVisitor
{
    public List<string> Tables { get; } = new();
    public List<string> Columns { get; } = new();
    public int JoinCount { get; private set; }
    public int SubqueryCount { get; private set; }
    public int WindowFunctionCount { get; private set; }
    public int CteCount { get; private set; }
    public List<AntiPattern> DetectedIssues { get; } = new();

    // Track context for anti-pattern detection
    private bool _isInWhereClause;
    private bool _isInJoinClause;
    private bool _hasGroupBy;
    private bool _hasUnion;
    private AntiPatternContext _context = new();

    // Track columns for statistics analysis
    private List<string> _whereColumns = new();
    private List<string> _joinColumns = new();
    private List<string> _orderByColumns = new();
    private List<string> _groupByColumns = new();

    public override void Visit(NamedTableReference node)
    {
        // Extract table names
        var tableName = node.SchemaObject.BaseIdentifier.Value;

        // Check for schema prefix (AP-13)
        if (node.SchemaObject.SchemaIdentifier == null)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-13",
                Severity = Severity.Warning,
                Title = "Missing schema prefix",
                Description = $"Table '{tableName}' should have schema prefix (e.g., dbo.{tableName})",
                Impact = "Schema resolution overhead",
                Location = node.StartLine
            });
        }

        if (!Tables.Contains(tableName))
            Tables.Add(tableName);

        base.Visit(node);
    }

    public override void Visit(QualifiedJoin node)
    {
        JoinCount++;
        _isInJoinClause = true;

        base.Visit(node);
        _isInJoinClause = false;
    }

    public override void Visit(UnqualifiedJoin node)
    {
        JoinCount++;

        // AP-17: Cross JOIN without WHERE
        if (node.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-17",
                Severity = Severity.Warning,
                Title = "CROSS JOIN detected",
                Description = "Cross join may produce Cartesian product",
                Impact = "Potentially huge result set, performance issue",
                Location = node.StartLine
            });
        }

        base.Visit(node);
    }

    public override void Visit(ScalarSubquery node)
    {
        SubqueryCount++;
        base.Visit(node);
    }

    public override void Visit(CommonTableExpression node)
    {
        CteCount++;
        base.Visit(node);
    }

    public override void Visit(SelectStarExpression node)
    {
        // AP-01: SELECT * detected
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-01",
            Severity = Severity.Critical,
            Title = "SELECT * detected",
            Description = "Fetching all columns unnecessarily",
            Impact = "Network overhead, memory waste, breaks column-level security",
            Location = node.StartLine
        });

        base.Visit(node);
    }

    public override void Visit(WhereClause node)
    {
        _isInWhereClause = true;

        // Track WHERE columns
        TrackColumnsInExpression(node.SearchCondition, _whereColumns);

        base.Visit(node);
        _isInWhereClause = false;
    }

    private void TrackColumnsInExpression(BooleanExpression? expression, List<string> columnList)
    {
        if (expression == null) return;

        if (expression is BooleanComparisonExpression comp)
        {
            if (comp.FirstExpression is ColumnReferenceExpression colRef)
            {
                var colName = colRef.MultiPartIdentifier.Identifiers.Last().Value;
                if (!columnList.Contains(colName, StringComparer.OrdinalIgnoreCase))
                {
                    columnList.Add(colName);
                }
            }
        }
        else if (expression is BooleanBinaryExpression binary)
        {
            TrackColumnsInExpression(binary.FirstExpression, columnList);
            TrackColumnsInExpression(binary.SecondExpression, columnList);
        }
    }

    public override void Visit(LikePredicate node)
    {
        // AP-03: Non-SARGable LIKE
        if (node.FirstExpression is StringLiteral literal)
        {
            var pattern = literal.Value;
            if (pattern.StartsWith("%"))
            {
                DetectedIssues.Add(new AntiPattern
                {
                    Code = "AP-03",
                    Severity = Severity.Critical,
                    Title = "Non-SARGable LIKE pattern",
                    Description = $"LIKE '{pattern}' starts with wildcard",
                    Impact = "Cannot use index, full table scan required",
                    Location = node.StartLine
                });
            }
        }

        base.Visit(node);
    }

    public override void Visit(InPredicate node)
    {
        // AP-16: Large IN list
        if (node.Values.Count > 100)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-16",
                Severity = Severity.Warning,
                Title = "Large IN list",
                Description = $"IN clause with {node.Values.Count} values",
                Impact = "Consider using temp table or table-valued parameter",
                Location = node.StartLine
            });
        }

        base.Visit(node);
    }

    /// <summary>
    /// Calculate complexity score based on query structure
    /// </summary>
    public int CalculateComplexityScore()
    {
        return JoinCount * 2 +
               SubqueryCount * 3 +
               WindowFunctionCount * 4 +
               CteCount * 2 +
               Tables.Count;
    }

    // ========== NEW: Helper Methods for Column Tracking ==========

    public List<string> GetWhereClauseColumns() => _whereColumns;
    public List<string> GetJoinColumns() => _joinColumns;
    public List<string> GetOrderByColumns() => _orderByColumns;
    public List<string> GetGroupByColumns() => _groupByColumns;

    public List<string> GetCriticalColumns()
    {
        var critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        critical.UnionWith(GetWhereClauseColumns());
        critical.UnionWith(GetJoinColumns());
        critical.UnionWith(GetOrderByColumns());
        critical.UnionWith(GetGroupByColumns());
        return critical.ToList();
    }

    // ========== NEW: Context Inference ==========

    private AntiPatternContext InferContext(QuerySpecification node)
    {
        var context = new AntiPatternContext();

        // Analytical: has aggregates, window functions, GROUP BY
        context.IsAnalyticalQuery = HasAggregates(node) || WindowFunctionCount > 0 || _hasGroupBy;

        // Reporting: UNION + aggregates
        context.IsReportingQuery = _hasUnion && HasAggregates(node);

        return context;
    }

    private bool HasAggregates(QuerySpecification node)
    {
        // Check for aggregate functions in SELECT
        if (node.SelectElements == null) return false;

        foreach (var element in node.SelectElements)
        {
            if (element is SelectScalarExpression scalar)
            {
                if (scalar.Expression is FunctionCall func)
                {
                    var funcName = func.FunctionName.Value.ToUpper();
                    if (funcName is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX")
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // ========== NEW: Anti-Pattern Detection Methods ==========

    public override void Visit(QuerySpecification node)
    {
        // Infer context for intelligent suppression
        _context = InferContext(node);

        // AP-11: Missing table alias in multi-table query
        if (node.FromClause?.TableReferences.Count > 1)
        {
            foreach (var tableRef in node.FromClause.TableReferences)
            {
                if (tableRef is NamedTableReference namedTable && namedTable.Alias == null)
                {
                    DetectedIssues.Add(new AntiPattern
                    {
                        Code = "AP-11",
                        Severity = Severity.Warning,
                        Title = "Missing table alias in multi-table query",
                        Description = $"Table '{namedTable.SchemaObject.BaseIdentifier.Value}' should have an alias for clarity",
                        Impact = "Reduced readability, ambiguous column references",
                        Location = node.StartLine,
                        Category = PatternCategory.CodeQuality
                    });
                }
            }
        }

        // AP-07: DISTINCT usage (with context awareness)
        if (node.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            // Only warn if NOT analytical query
            if (!_context.IsAnalyticalQuery && !_context.HasUniqueConstraints)
            {
                DetectedIssues.Add(new AntiPattern
                {
                    Code = "AP-07",
                    Severity = Severity.Info,
                    Title = "DISTINCT usage detected",
                    Description = "Verify if DISTINCT is necessary. May hide data quality issues.",
                    Impact = "Additional sorting/hashing overhead",
                    Location = node.StartLine,
                    SuppressInAnalyticalContext = true,
                    Category = PatternCategory.Performance
                });
            }
        }

        // AP-23: Missing WHERE clause (Info severity, suppress in analytical)
        if (node.WhereClause == null && node.FromClause != null)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-23",
                Severity = Severity.Info,
                Title = "Query without WHERE clause",
                Description = "SELECT without WHERE may return entire table. Verify if intentional.",
                Impact = "Potential full table scan, large result set",
                Location = node.StartLine,
                SuppressInAnalyticalContext = true,
                Category = PatternCategory.Performance
            });
        }

        base.Visit(node);
    }

    public override void Visit(GroupByClause node)
    {
        _hasGroupBy = true;

        // Track GROUP BY columns
        foreach (var grouping in node.GroupingSpecifications)
        {
            if (grouping is ExpressionGroupingSpecification exprGroup)
            {
                if (exprGroup.Expression is ColumnReferenceExpression colRef)
                {
                    var colName = colRef.MultiPartIdentifier.Identifiers.Last().Value;
                    _groupByColumns.Add(colName);
                }
            }
        }

        base.Visit(node);
    }

    public override void Visit(HavingClause node)
    {
        // AP-09: HAVING without GROUP BY
        if (!_hasGroupBy)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-09",
                Severity = Severity.Error,
                Title = "HAVING without GROUP BY",
                Description = "HAVING clause requires GROUP BY. Use WHERE instead for row filtering.",
                Impact = "Logic error, incorrect results",
                Location = node.StartLine,
                Category = PatternCategory.Logic
            });
        }

        base.Visit(node);
    }

    public override void Visit(BinaryQueryExpression node)
    {
        // AP-08: UNION vs UNION ALL (Info severity)
        if (node.BinaryQueryExpressionType == BinaryQueryExpressionType.Union)
        {
            _hasUnion = true;

            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-08",
                Severity = Severity.Info,
                Title = "UNION detected",
                Description = "UNION removes duplicates. If duplicates are impossible or acceptable, use UNION ALL for better performance.",
                Impact = "Unnecessary sorting/deduplication overhead",
                Location = node.StartLine,
                AutoFixSuggestion = "Consider UNION ALL if duplicates are acceptable",
                Category = PatternCategory.Performance
            });
        }

        base.Visit(node);
    }

    public override void Visit(SelectScalarExpression node)
    {
        // AP-12: N+1 Query Pattern (Subquery in SELECT)
        if (node.Expression is ScalarSubquery)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-12",
                Severity = Severity.Critical,
                Title = "Subquery in SELECT clause (N+1 pattern)",
                Description = "Scalar subquery in SELECT executes once per row. Consider JOIN instead.",
                Impact = "Severe performance degradation, O(n²) complexity",
                Location = node.StartLine,
                Category = PatternCategory.Performance
            });
        }

        base.Visit(node);
    }

    public override void Visit(BooleanComparisonExpression node)
    {
        // AP-10: Implicit CAST detection
        if (node.FirstExpression is ColumnReferenceExpression col &&
            node.SecondExpression is StringLiteral literal)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-10",
                Severity = Severity.Warning,
                Title = "Potential implicit conversion",
                Description = "String literal compared to typed column may cause implicit conversion.",
                Impact = "Index may not be used, performance degradation",
                Location = node.StartLine,
                Category = PatternCategory.SARGability
            });
        }

        // AP-21: Missing N prefix for nvarchar
        if (node.SecondExpression is StringLiteral stringLit &&
            !stringLit.Value.StartsWith("N'"))
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-21",
                Severity = Severity.Warning,
                Title = "Potential varchar/nvarchar mismatch",
                Description = "String literal without N prefix may cause implicit conversion if column is nvarchar.",
                Impact = "Index scan instead of seek, performance degradation",
                Location = node.StartLine,
                AutoFixSuggestion = $"Use N'{stringLit.Value}' for nvarchar columns",
                ConfidenceLevel = ConfidenceLevel.Medium,
                Category = PatternCategory.SARGability
            });
        }

        base.Visit(node);
    }

    public override void Visit(BooleanBinaryExpression node)
    {
        // AP-06: OR → IN conversion
        if (node.BinaryExpressionType == BooleanBinaryExpressionType.Or)
        {
            var orChain = ExtractOrChain(node);
            if (orChain.Count >= 3 && AllSameColumn(orChain))
            {
                DetectedIssues.Add(new AntiPattern
                {
                    Code = "AP-06",
                    Severity = Severity.Warning,
                    Title = "Multiple OR conditions on same column",
                    Description = $"Found {orChain.Count} OR conditions. Consider using IN clause.",
                    Impact = "Less readable, potentially less efficient",
                    Location = node.StartLine,
                    AutoFixSuggestion = "Convert to IN clause",
                    ConfidenceLevel = ConfidenceLevel.Medium,
                    Category = PatternCategory.CodeQuality
                });
            }
        }

        base.Visit(node);
    }

    private List<BooleanExpression> ExtractOrChain(BooleanBinaryExpression node)
    {
        var chain = new List<BooleanExpression>();

        if (node.BinaryExpressionType == BooleanBinaryExpressionType.Or)
        {
            if (node.FirstExpression is BooleanBinaryExpression leftOr)
            {
                chain.AddRange(ExtractOrChain(leftOr));
            }
            else
            {
                chain.Add(node.FirstExpression);
            }

            chain.Add(node.SecondExpression);
        }

        return chain;
    }

    private bool AllSameColumn(List<BooleanExpression> expressions)
    {
        string? firstColumn = null;

        foreach (var expr in expressions)
        {
            if (expr is BooleanComparisonExpression comp &&
                comp.FirstExpression is ColumnReferenceExpression colRef)
            {
                var colName = colRef.MultiPartIdentifier.Identifiers.Last().Value;

                if (firstColumn == null)
                {
                    firstColumn = colName;
                }
                else if (!string.Equals(firstColumn, colName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return firstColumn != null;
    }

    public override void Visit(CreateProcedureStatement node)
    {
        // AP-14: Missing SET NOCOUNT ON
        var hasNoCount = CheckForSetNoCount(node.StatementList);

        if (!hasNoCount)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-14",
                Severity = Severity.Info,
                Title = "Missing SET NOCOUNT ON",
                Description = "Stored procedure should include SET NOCOUNT ON to reduce network traffic.",
                Impact = "Minor performance overhead",
                Location = node.StartLine,
                AutoFixSuggestion = "Add 'SET NOCOUNT ON;' at procedure start",
                ConfidenceLevel = ConfidenceLevel.High,
                Category = PatternCategory.Performance
            });
        }

        base.Visit(node);
    }

    private bool CheckForSetNoCount(StatementList? statementList)
    {
        if (statementList == null) return false;

        foreach (var statement in statementList.Statements)
        {
            // Check if it's a SET statement (simplified check)
            var statementText = statement.GetType().Name;
            if (statementText.Contains("Set", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public override void Visit(OverClause node)
    {
        WindowFunctionCount++;

        // AP-18: ROW_NUMBER for pagination
        if (node.WindowFrameClause == null && node.OrderByClause != null)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-18",
                Severity = Severity.Info,
                Title = "ROW_NUMBER for pagination",
                Description = @"Consider pagination alternatives:
- Small datasets (<10k rows): OFFSET/FETCH is acceptable
- Large datasets + high page numbers: Use Keyset Pagination (WHERE Id > @LastId)
- Keyset provides O(1) performance regardless of page number",
                Impact = "OFFSET/FETCH or Keyset may perform better depending on dataset size",
                Location = node.StartLine,
                AutoFixSuggestion = "OFFSET x ROWS FETCH NEXT y ROWS ONLY (or Keyset for large datasets)",
                ConfidenceLevel = ConfidenceLevel.Low,
                Category = PatternCategory.Performance
            });
        }

        base.Visit(node);
    }

    public override void Visit(FunctionCall node)
    {
        var functionName = node.FunctionName.Value.ToUpper();

        // AP-04: COUNT(*) vs COUNT(pk)
        if (functionName == "COUNT")
        {
            if (node.Parameters.Count == 0 || node.Parameters[0] is SelectStarExpression)
            {
                DetectedIssues.Add(new AntiPattern
                {
                    Code = "AP-04",
                    Severity = Severity.Warning,
                    Title = "COUNT(*) detected",
                    Description = "COUNT(*) counts all rows including NULLs. Consider COUNT(1) or COUNT(pk) for better clarity.",
                    Impact = "Slightly less efficient, semantic ambiguity",
                    Location = node.StartLine,
                    AutoFixSuggestion = "COUNT(1)",
                    ConfidenceLevel = ConfidenceLevel.Medium,
                    Category = PatternCategory.CodeQuality
                });
            }
        }

        // AP-02: Function on indexed column (non-SARGable)
        if (_isInWhereClause || _isInJoinClause)
        {
            if (functionName is "YEAR" or "MONTH" or "DAY" or "DATEPART" or
                "UPPER" or "LOWER" or "SUBSTRING" or "LEFT" or "RIGHT")
            {
                DetectedIssues.Add(new AntiPattern
                {
                    Code = "AP-02",
                    Severity = Severity.Critical,
                    Title = "Function on indexed column (non-SARGable)",
                    Description = $"Function {functionName}() in WHERE/JOIN prevents index usage",
                    Impact = "Full table scan, poor performance",
                    Location = node.StartLine,
                    Category = PatternCategory.SARGability
                });
            }
        }

        // AP-15: ISNULL/COALESCE in WHERE
        if (_isInWhereClause && (functionName is "ISNULL" or "COALESCE"))
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-15",
                Severity = Severity.Warning,
                Title = "ISNULL/COALESCE in WHERE clause",
                Description = "May prevent index usage (non-SARGable)",
                Impact = "Potential full table scan",
                Location = node.StartLine,
                Category = PatternCategory.SARGability
            });
        }

        base.Visit(node);
    }

    public override void Visit(OrderByClause node)
    {
        // Track ORDER BY columns
        foreach (var element in node.OrderByElements)
        {
            if (element.Expression is ColumnReferenceExpression colRef)
            {
                var colName = colRef.MultiPartIdentifier.Identifiers.Last().Value;
                _orderByColumns.Add(colName);
            }
        }

        base.Visit(node);
    }
}
