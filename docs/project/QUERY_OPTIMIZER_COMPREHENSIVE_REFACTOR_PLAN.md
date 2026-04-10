# 📋 QUERY OPTIMIZER - KẾ HOẠCH REFACTOR TOÀN DIỆN

## 🎯 MỤC TIÊU CHÍNH

Nâng cấp Query Optimizer từ "passive analyzer" thành "intelligent optimization engine" với khả năng:
- Phát hiện 25+ SQL Server anti-patterns (hiện tại chỉ có 5)
- Tích hợp column statistics và data skew analysis vào pipeline
- Cung cấp data-driven insights cho LLM thay vì để LLM "đoán"
- Auto-fix các patterns đơn giản không cần LLM
- Execution plan analysis với warnings và cost breakdown chi tiết

---

## 🐛 ROOT CAUSE ANALYSIS

### ⚠️ CRITICAL GAP 0: SQL Server 2022 Native Anti-Pattern Detection - BỊ BỎ QUA HOÀN TOÀN
**Hiện trạng:** Plan đang build lại một phần thứ SQL Server đã làm native

**SQL Server 2022 đã có:**
- `query_antipattern` Extended Events
- `sys.dm_xe_map_values` với `name = N'query_antipattern_type'`
- Query Store enabled by default với anti-pattern tracking

**Impact:** Đang reinvent the wheel thay vì leverage native capabilities

**Cần thêm:** Layer 0.5 - SQL Server Native Anti-Pattern Query
```csharp
// Query native anti-patterns từ SQL Server
SELECT map_value AS AntiPatternType 
FROM sys.dm_xe_map_values  
WHERE name = N'query_antipattern_type'
```

**References:**
- [SQL Server Anti-Pattern Detection](https://learn.microsoft.com/en-us/sql/relational-databases/extended-events/use-the-system-health-session)
- [Query Store Integration](https://learn.microsoft.com/en-us/sql/relational-databases/performance/monitoring-performance-by-using-the-query-store)

---

### Vấn đề 1: Layer 1 (StaticAnalyzer) - THIẾU TRẦM TRỌNG
**Hiện trạng:** Chỉ detect 5 anti-patterns (AP-01, AP-02, AP-03, AP-13, AP-15, AP-16, AP-17)

**Thiếu:** 20+ patterns quan trọng khác:
- AP-04: Implicit column type (COUNT(*) vs COUNT(pk))
- AP-05: Missing index usage analysis
- AP-06: OR → IN conversion
- AP-07: DISTINCT abuse
- AP-08: UNION vs UNION ALL
- AP-09: HAVING without GROUP BY
- AP-10: Implicit CAST/conversion
- AP-11: Table alias missing
- AP-12: N+1 Query Pattern
- AP-14: Missing SET NOCOUNT
- AP-18: CTID/ROW_NUMBER abuse
- AP-19: Parameter sniffing indicators
- AP-20: Stale statistics warnings
- AP-21: Implicit conversion (varchar/nvarchar mismatch)
- AP-22: Nested loops on large tables
- AP-23: Missing WHERE clause on large tables
- AP-24: Cursor patterns
- AP-25: Temp table without indexes

### Vấn đề 2: Layer 2 (ColumnStatistics) - KHÔNG ĐƯỢC SỬ DỤNG
**Hiện trạng:** Service đã implement đầy đủ nhưng KHÔNG BAO GIỜ được gọi trong pipeline

**Impact:** LLM không nhận được:
- Data skew factors
- Selectivity metrics
- Top values frequency
- Index effectiveness predictions


### Vấn đề 3: Layer 3 (LLM Prompt) - THIẾU QUANTITATIVE DATA
**Hiện trạng:** Prompt chỉ có schema text thuần túy

**Thiếu:**
- Column statistics với skew/selectivity
- Execution plan operators và warnings
- Cost breakdown chi tiết
- Index usage analysis
- Data-driven recommendations

### Vấn đề 4: Layer 4 (ExecutionPlan) - CHỈ PARSE CƠ BẢN
**Hiện trạng:** Chỉ extract operators và cost đơn giản

**Thiếu:**
- Nested loop join cost analysis
- Missing statistics warnings detection
- Stale stats detection
- Operator-level cardinality analysis
- Index seek vs scan recommendations
- Implicit conversion warnings

---

## 📊 PROPOSED ARCHITECTURE

### New Flow (Data-Driven Intelligence)

```
SQL Input
    ↓
[Layer 0: Pre-Flight Check] ← NEW
    ├── Quick complexity assessment
    ├── Determine if optimization needed
    └── Route to appropriate strategy
    ↓
[Layer 0.5: SQL Server Native Detection] ← NEW CRITICAL
    ├── Query sys.dm_xe_map_values for native anti-patterns
    ├── Check Query Store for historical performance
    ├── Detect compatibility level (PSP support check)
    ├── Check VIEW DATABASE STATE permission
    └── Merge with static analysis results
    ↓
[Layer 1: Enhanced StaticAnalyzer]
    ├── Detect 25+ anti-patterns
    ├── Extract WHERE/JOIN columns
    ├── Identify implicit conversions
    ├── Flag parameter sniffing risks
    ├── Check AntiPatternContext for false positive suppression
    └── Auto-fix simple patterns WITH semantic validation
    ↓
[Layer 2: SchemaEnricher + ColumnStatistics] ← ENHANCED
    ├── Full schema metadata
    ├── Column statistics for WHERE/JOIN columns
    ├── Data skew analysis
    ├── Selectivity calculations
    ├── Statistics freshness check (sys.dm_db_stats_properties)
    ├── DDL-triggered cache invalidation
    └── Index effectiveness predictions
    ↓
[Layer 3: ExecutionPlanAnalyzer] ← ENHANCED
    ├── Permission check (VIEW DATABASE STATE)
    ├── Get estimated plan for ORIGINAL query (with graceful degradation)
    ├── Parse warnings (missing stats, implicit conversions)
    ├── Identify cost drivers (top 3 expensive operators)
    ├── Extract cardinality estimates
    ├── Statistics staleness detection
    └── Generate index recommendations
    ↓
[Layer 4: LLM Optimization] ← ENHANCED PROMPT
    ├── Token budget management (ContextBudgetManager)
    ├── Anti-patterns list với severity
    ├── Column statistics (skew, selectivity)
    ├── Execution plan metrics
    ├── Cost breakdown
    ├── Compatibility level context (PSP support)
    ├── Data-driven recommendations
    └── Context-aware suggestions
    ↓
[Layer 5: Verification + Cost Analysis]
    ├── Get plan for OPTIMIZED query
    ├── Compare costs
    ├── Validate improvements
    └── Generate detailed report
    ↓
Output: Comprehensive optimization report ✅
```

---


## 🔧 IMPLEMENTATION PLAN

---

## PHASE 1: ENHANCED STATIC ANALYZER (Priority: CRITICAL)
**Timeline:** 2-3 days  
**Effort:** Medium  
**Impact:** High

### Task 1.1: Thêm 20+ Anti-Pattern Detection

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/QueryMetadataVisitor.cs`

**Patterns cần thêm:**

#### AP-04: Implicit Column Type Issues
```csharp
public override void Visit(FunctionCall node)
{
    var functionName = node.FunctionName.Value.ToUpper();
    
    // COUNT(*) vs COUNT(1) vs COUNT(pk)
    if (functionName == "COUNT")
    {
        if (node.Parameters.Count == 0 || node.Parameters[0] is AsteriskColumn)
        {
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-04",
                Severity = Severity.Warning,
                Title = "COUNT(*) detected",
                Description = "COUNT(*) counts all rows including NULLs. Consider COUNT(1) or COUNT(pk) for better clarity.",
                Impact = "Slightly less efficient, semantic ambiguity",
                Location = node.StartLine,
                AutoFixSuggestion = "COUNT(1)" // ← Auto-fix capability
            });
        }
    }
    
    base.Visit(node);
}
```

#### AP-06: OR → IN Conversion
```csharp
public override void Visit(BooleanBinaryExpression node)
{
    // Detect: col='A' OR col='B' OR col='C'
    // Suggest: col IN ('A','B','C')
    
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
                AutoFixSuggestion = GenerateInClause(orChain)
            });
        }
    }
    
    base.Visit(node);
}
```

#### AP-07: DISTINCT Abuse
```csharp
public override void Visit(SelectStatement node)
{
    if (node.QueryExpression is QuerySpecification spec)
    {
        if (spec.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            // Check if DISTINCT is necessary
            // If all selected columns are from a unique index, DISTINCT is redundant
            DetectedIssues.Add(new AntiPattern
            {
                Code = "AP-07",
                Severity = Severity.Warning,
                Title = "DISTINCT usage detected",
                Description = "Verify if DISTINCT is necessary. May hide data quality issues.",
                Impact = "Additional sorting/hashing overhead",
                Location = node.StartLine
            });
        }
    }
    
    base.Visit(node);
}
```


#### AP-08: UNION vs UNION ALL
```csharp
public override void Visit(BinaryQueryExpression node)
{
    if (node.BinaryQueryExpressionType == BinaryQueryExpressionType.Union)
    {
        // Check if UNION ALL would be more appropriate
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-08",
            Severity = Severity.Info,
            Title = "UNION detected",
            Description = "UNION removes duplicates. If duplicates are impossible or acceptable, use UNION ALL for better performance.",
            Impact = "Unnecessary sorting/deduplication overhead",
            Location = node.StartLine,
            AutoFixSuggestion = "Consider UNION ALL if duplicates are acceptable"
        });
    }
    
    base.Visit(node);
}
```

#### AP-09: HAVING without GROUP BY
```csharp
public override void Visit(HavingClause node)
{
    // Track if we're in a query with GROUP BY
    if (!_hasGroupBy)
    {
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-09",
            Severity = Severity.Error,
            Title = "HAVING without GROUP BY",
            Description = "HAVING clause requires GROUP BY. Use WHERE instead for row filtering.",
            Impact = "Logic error, incorrect results",
            Location = node.StartLine
        });
    }
    
    base.Visit(node);
}
```

#### AP-10: Implicit CAST Detection
```csharp
public override void Visit(BooleanComparisonExpression node)
{
    // Detect: WHERE date_column = '2024-01-01' (string literal)
    // Should be: WHERE date_column = CAST('2024-01-01' AS DATE)
    
    if (node.FirstExpression is ColumnReferenceExpression col &&
        node.SecondExpression is StringLiteral literal)
    {
        // Check if column is date/datetime type (requires schema context)
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-10",
            Severity = Severity.Warning,
            Title = "Potential implicit conversion",
            Description = "String literal compared to typed column may cause implicit conversion.",
            Impact = "Index may not be used, performance degradation",
            Location = node.StartLine
        });
    }
    
    base.Visit(node);
}
```

#### AP-11: Missing Table Alias
```csharp
public override void Visit(QuerySpecification node)
{
    if (node.FromClause?.TableReferences.Count > 1)
    {
        // Multi-table query
        foreach (var tableRef in node.FromClause.TableReferences)
        {
            if (tableRef is NamedTableReference namedTable && namedTable.Alias == null)
            {
                DetectedIssues.Add(new AntiPattern
                {
                    Code = "AP-11",
                    Severity = Severity.Warning,
                    Title = "Missing table alias in multi-table query",
                    Description = $"Table '{namedTable.SchemaObject.BaseIdentifier.Value}' should have an alias for clarity.",
                    Impact = "Reduced readability, ambiguous column references",
                    Location = node.StartLine
                });
            }
        }
    }
    
    base.Visit(node);
}
```

#### AP-12: N+1 Query Pattern (Subquery in SELECT)
```csharp
public override void Visit(SelectScalarExpression node)
{
    if (node.Expression is ScalarSubquery)
    {
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-12",
            Severity = Severity.Critical,
            Title = "Subquery in SELECT clause (N+1 pattern)",
            Description = "Scalar subquery in SELECT executes once per row. Consider JOIN instead.",
            Impact = "Severe performance degradation, O(n²) complexity",
            Location = node.StartLine
        });
    }
    
    base.Visit(node);
}
```


#### AP-14: Missing SET NOCOUNT
```csharp
// This requires stored procedure detection
public override void Visit(CreateProcedureStatement node)
{
    // Check if SET NOCOUNT ON exists in procedure body
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
            AutoFixSuggestion = "Add 'SET NOCOUNT ON;' at procedure start"
        });
    }
    
    base.Visit(node);
}
```

#### AP-18: ROW_NUMBER Abuse for Pagination
```csharp
public override void Visit(OverClause node)
{
    // Detect ROW_NUMBER() OVER (ORDER BY ...) pattern
    // Suggest OFFSET/FETCH instead
    
    if (node.WindowFrameClause == null && node.OrderByClause != null)
    {
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-18",
            Severity = Severity.Info,
            Title = "ROW_NUMBER for pagination",
            Description = "Consider using OFFSET/FETCH for pagination instead of ROW_NUMBER.",
            Impact = "OFFSET/FETCH is more readable and may perform better",
            Location = node.StartLine,
            AutoFixSuggestion = "Use OFFSET x ROWS FETCH NEXT y ROWS ONLY"
        });
    }
    
    base.Visit(node);
}
```

#### AP-21: Implicit Conversion (varchar/nvarchar)
```csharp
// This requires schema context to detect type mismatches
public override void Visit(BooleanComparisonExpression node)
{
    // Will be enhanced in Phase 2 with schema context
    // Detect: WHERE nvarchar_column = 'literal' (varchar)
    // Should be: WHERE nvarchar_column = N'literal' (nvarchar)
    
    if (node.SecondExpression is StringLiteral literal && 
        !literal.Value.StartsWith("N'"))
    {
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-21",
            Severity = Severity.Warning,
            Title = "Potential varchar/nvarchar mismatch",
            Description = "String literal without N prefix may cause implicit conversion if column is nvarchar.",
            Impact = "Index scan instead of seek, performance degradation",
            Location = node.StartLine,
            AutoFixSuggestion = $"Use N'{literal.Value}' for nvarchar columns"
        });
    }
    
    base.Visit(node);
}
```

#### AP-23: Missing WHERE Clause on Large Tables
```csharp
public override void Visit(QuerySpecification node)
{
    if (node.WhereClause == null && node.FromClause != null)
    {
        // Will be enhanced with row count check in Phase 2
        DetectedIssues.Add(new AntiPattern
        {
            Code = "AP-23",
            Severity = Severity.Warning,
            Title = "Query without WHERE clause",
            Description = "SELECT without WHERE may return entire table. Verify if intentional.",
            Impact = "Potential full table scan, large result set",
            Location = node.StartLine
        });
    }
    
    base.Visit(node);
}
```

### Task 1.2: Thêm Helper Methods

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/QueryMetadataVisitor.cs`

```csharp
/// <summary>
/// Extract columns used in WHERE clause
/// </summary>
public List<string> GetWhereClauseColumns()
{
    // Parse WHERE clause and extract column names
    return _whereColumns;
}

/// <summary>
/// Extract columns used in JOIN conditions
/// </summary>
public List<string> GetJoinColumns()
{
    return _joinColumns;
}

/// <summary>
/// Extract columns in ORDER BY
/// </summary>
public List<string> GetOrderByColumns()
{
    return _orderByColumns;
}

/// <summary>
/// Extract columns in GROUP BY
/// </summary>
public List<string> GetGroupByColumns()
{
    return _groupByColumns;
}

/// <summary>
/// Get all columns that should have statistics analyzed
/// </summary>
public List<string> GetCriticalColumns()
{
    var critical = new HashSet<string>();
    critical.UnionWith(GetWhereClauseColumns());
    critical.UnionWith(GetJoinColumns());
    critical.UnionWith(GetOrderByColumns());
    critical.UnionWith(GetGroupByColumns());
    return critical.ToList();
}
```


### Task 1.3: Auto-Fix Simple Patterns

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/AutoFixer.cs` (NEW)

```csharp
namespace TextToSqlAgent.Application.Services.QueryOptimizer;

/// <summary>
/// Automatically fixes simple anti-patterns without LLM
/// </summary>
public class AutoFixer
{
    /// <summary>
    /// Fix SELECT * by expanding to explicit columns
    /// </summary>
    public string FixSelectStar(string sql, SchemaContext schema)
    {
        // Parse SQL, find SELECT *, replace with explicit columns
        // Example: SELECT * FROM Users → SELECT Id, Name, Email FROM Users
    }
    
    /// <summary>
    /// Add missing schema prefix (dbo.)
    /// </summary>
    public string FixMissingSchema(string sql, string defaultSchema = "dbo")
    {
        // Parse SQL, add schema prefix to tables without one
        // Example: SELECT * FROM Users → SELECT * FROM dbo.Users
    }
    
    /// <summary>
    /// Convert OR chain to IN clause
    /// </summary>
    public string FixOrToIn(string sql)
    {
        // col='A' OR col='B' OR col='C' → col IN ('A','B','C')
    }
    
    /// <summary>
    /// Add N prefix to string literals for nvarchar columns
    /// </summary>
    public string FixNvarcharLiterals(string sql, SchemaContext schema)
    {
        // WHERE Name = 'John' → WHERE Name = N'John' (if Name is nvarchar)
    }
    
    /// <summary>
    /// Determine if query can be auto-fixed
    /// </summary>
    public bool CanAutoFix(List<AntiPattern> issues)
    {
        var autoFixableCodes = new[] { "AP-01", "AP-06", "AP-13", "AP-21" };
        return issues.All(i => autoFixableCodes.Contains(i.Code));
    }
}
```

---

## PHASE 2: COLUMN STATISTICS INTEGRATION (Priority: CRITICAL)
**Timeline:** 2-3 days  
**Effort:** Medium  
**Impact:** Very High

### Task 2.1: Integrate ColumnStatistics into Pipeline

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`

**Modifications:**

```csharp
private async Task<OptimizationResult> OptimizeWithLLMAsync(
    string sql,
    QueryMetadata metadata,
    SchemaContext schemaContext,
    string modelName,
    CancellationToken cancellationToken)
{
    // ========== NEW: Get column statistics ==========
    var criticalColumns = metadata.GetCriticalColumns();
    var columnStats = new Dictionary<string, ColumnStatistics>();
    
    foreach (var table in metadata.Tables)
    {
        var tableColumns = criticalColumns
            .Where(c => BelongsToTable(c, table))
            .ToList();
            
        if (tableColumns.Any())
        {
            var stats = await _columnStatisticsService.GetTableStatisticsAsync(
                table,
                tableColumns,
                connectionString,
                cancellationToken);
                
            foreach (var kvp in stats)
            {
                columnStats[$"{table}.{kvp.Key}"] = kvp.Value;
            }
        }
    }
    
    // ========== Build enhanced context ==========
    var detectedIssuesText = BuildIssuesText(metadata.DetectedIssues);
    var schemaContextText = BuildSchemaContextText(schemaContext);
    var columnStatsText = BuildColumnStatsText(columnStats); // ← NEW
    
    // ========== Load and populate prompt ==========
    var promptTemplate = await LoadPromptTemplate();
    var prompt = promptTemplate
        .Replace("{{$detected_issues}}", detectedIssuesText)
        .Replace("{{$schema_context}}", schemaContextText)
        .Replace("{{$column_statistics}}", columnStatsText) // ← NEW
        .Replace("{{$original_sql}}", sql);
    
    // ... rest of method
}
```

### Task 2.2: Build Column Statistics Text

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`

```csharp
/// <summary>
/// Build column statistics section for LLM prompt
/// </summary>
private string BuildColumnStatsText(Dictionary<string, ColumnStatistics> stats)
{
    if (!stats.Any())
        return "No column statistics available.";
    
    var lines = new List<string>();
    lines.Add("⚠️ COLUMN STATISTICS & DATA SKEW ANALYSIS:");
    lines.Add("");
    
    foreach (var kvp in stats.OrderByDescending(x => x.Value.SkewFactor))
    {
        var col = kvp.Key;
        var stat = kvp.Value;
        
        lines.Add($"Column: {col}");
        lines.Add($"  - Total Rows: {stat.TotalRows:N0}");
        lines.Add($"  - Distinct Values: {stat.DistinctValues:N0}");
        lines.Add($"  - Selectivity: {stat.Selectivity:P2} ({GetSelectivityLevel(stat.Selectivity)})");
        lines.Add($"  - Skew Factor: {stat.SkewFactor:P2} ({stat.SkewLevel})");
        lines.Add($"  - Index Recommendation: {stat.IndexRecommendation}");
        
        if (stat.TopValues.Any())
        {
            lines.Add($"  - Top Values:");
            foreach (var tv in stat.TopValues.Take(5))
            {
                lines.Add($"      '{tv.Value}': {tv.Count:N0} rows ({tv.Percentage}%)");
            }
        }
        
        // Add optimization hints based on statistics
        if (stat.SkewFactor > 0.7)
        {
            lines.Add($"  ⚠️ HIGH SKEW WARNING: Index may not be used for majority value!");
            lines.Add($"     Consider: Filtered index, partition, or parameter sniffing mitigation");
        }
        
        if (stat.Selectivity < 0.01)
        {
            lines.Add($"  ⚠️ LOW SELECTIVITY: Very few distinct values");
            lines.Add($"     Consider: Filtered index for minority values only");
        }
        
        lines.Add("");
    }
    
    return string.Join("\n", lines);
}

private string GetSelectivityLevel(double selectivity)
{
    if (selectivity > 0.5) return "High - Excellent for indexing";
    if (selectivity > 0.1) return "Moderate - Good for indexing";
    if (selectivity > 0.01) return "Low - Limited index benefit";
    return "Very Low - Index not recommended";
}
```


---

## PHASE 3: EXECUTION PLAN ENHANCEMENT (Priority: HIGH)
**Timeline:** 3-4 days  
**Effort:** High  
**Impact:** Very High

### Task 3.1: Pre-Flight Execution Plan Check

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/ExecutionPlanService.cs`

**New Methods:**

```csharp
/// <summary>
/// Pre-flight check: Analyze original query BEFORE optimization
/// Returns detailed insights for LLM context
/// </summary>
public async Task<PreFlightAnalysis> GetPreFlightAnalysisAsync(
    string sql,
    string connectionString,
    CancellationToken cancellationToken = default)
{
    var plan = await GetEstimatedPlanAsync(sql, connectionString, cancellationToken);
    
    return new PreFlightAnalysis
    {
        EstimatedCost = plan.EstimatedTotalCost,
        EstimatedRows = plan.EstimatedRows,
        CostDrivers = IdentifyCostDrivers(plan.Operators),
        Warnings = ParseWarnings(plan.Warnings),
        IndexRecommendations = ExtractIndexRecommendations(plan),
        ImplicitConversions = DetectImplicitConversions(plan),
        MissingStatistics = DetectMissingStatistics(plan),
        ExpensiveOperators = GetExpensiveOperators(plan.Operators),
        NeedsOptimization = DetermineIfOptimizationNeeded(plan)
    };
}

/// <summary>
/// Identify top 3 cost drivers in execution plan
/// </summary>
private List<CostDriver> IdentifyCostDrivers(List<PlanOperator> operators)
{
    return operators
        .OrderByDescending(op => op.EstimatedCost)
        .Take(3)
        .Select(op => new CostDriver
        {
            OperatorType = op.Type,
            Cost = op.EstimatedCost,
            Rows = op.EstimatedRows,
            ObjectName = op.ObjectName,
            IndexName = op.IndexName,
            Description = FormatCostDriverDescription(op)
        })
        .ToList();
}

private string FormatCostDriverDescription(PlanOperator op)
{
    var desc = $"{op.Type}";
    
    if (!string.IsNullOrEmpty(op.ObjectName))
        desc += $" on {op.ObjectName}";
        
    if (!string.IsNullOrEmpty(op.IndexName))
        desc += $" using {op.IndexName}";
        
    desc += $" (Cost: {op.EstimatedCost:F2}, Rows: {op.EstimatedRows:N0})";
    
    // Add recommendations
    if (op.Type == "Clustered Index Scan" || op.Type == "Table Scan")
    {
        desc += " ⚠️ Full scan detected - consider adding index";
    }
    else if (op.Type == "Nested Loops" && op.EstimatedRows > 10000)
    {
        desc += " ⚠️ Nested loop on large dataset - consider hash/merge join";
    }
    
    return desc;
}

/// <summary>
/// Parse execution plan warnings into actionable insights
/// </summary>
private List<PlanWarning> ParseWarnings(List<string> rawWarnings)
{
    var warnings = new List<PlanWarning>();
    
    foreach (var warning in rawWarnings)
    {
        var parsed = new PlanWarning { RawWarning = warning };
        
        if (warning.Contains("NoJoinPredicate"))
        {
            parsed.Type = WarningType.MissingJoinPredicate;
            parsed.Severity = WarningSeverity.Critical;
            parsed.Description = "Missing JOIN predicate - Cartesian product detected";
            parsed.Recommendation = "Add proper JOIN condition to avoid Cartesian product";
        }
        else if (warning.Contains("ColumnsWithNoStatistics"))
        {
            parsed.Type = WarningType.MissingStatistics;
            parsed.Severity = WarningSeverity.High;
            parsed.Description = "Missing statistics on columns used in query";
            parsed.Recommendation = "Run UPDATE STATISTICS or CREATE STATISTICS";
        }
        else if (warning.Contains("UnmatchedIndexes"))
        {
            parsed.Type = WarningType.UnmatchedIndexes;
            parsed.Severity = WarningSeverity.Medium;
            parsed.Description = "Query could benefit from additional indexes";
            parsed.Recommendation = "Review missing index suggestions";
        }
        else if (warning.Contains("SpillToTempDb"))
        {
            parsed.Type = WarningType.SpillToTempDb;
            parsed.Severity = WarningSeverity.High;
            parsed.Description = "Sort/Hash operation spilling to tempdb";
            parsed.Recommendation = "Increase memory grant or optimize query";
        }
        
        warnings.Add(parsed);
    }
    
    return warnings;
}

/// <summary>
/// Detect implicit conversions from execution plan
/// </summary>
private List<ImplicitConversion> DetectImplicitConversions(ExecutionPlan plan)
{
    var conversions = new List<ImplicitConversion>();
    
    // Parse plan XML for CONVERT_IMPLICIT warnings
    // This requires deeper XML parsing
    
    foreach (var op in plan.Operators)
    {
        // Check for implicit conversion indicators in operator properties
        // SQL Server marks these in the plan XML
    }
    
    return conversions;
}

/// <summary>
/// Extract missing index recommendations from plan
/// </summary>
private List<IndexRecommendation> ExtractIndexRecommendations(ExecutionPlan plan)
{
    var recommendations = new List<IndexRecommendation>();
    
    // Parse MissingIndexes from plan XML
    // SQL Server provides these automatically
    
    return recommendations;
}

/// <summary>
/// Get operators that consume >10% of total cost
/// </summary>
private List<PlanOperator> GetExpensiveOperators(List<PlanOperator> operators)
{
    var totalCost = operators.Sum(op => op.EstimatedCost);
    var threshold = totalCost * 0.1;
    
    return operators
        .Where(op => op.EstimatedCost > threshold)
        .OrderByDescending(op => op.EstimatedCost)
        .ToList();
}

/// <summary>
/// Determine if query needs optimization based on plan analysis
/// </summary>
private bool DetermineIfOptimizationNeeded(ExecutionPlan plan)
{
    // Simple query threshold
    if (plan.EstimatedTotalCost < 0.1) return false;
    
    // Has warnings
    if (plan.Warnings.Any()) return true;
    
    // Has expensive scans
    var hasExpensiveScans = plan.Operators.Any(op => 
        (op.Type.Contains("Scan") && op.EstimatedCost > 1.0));
    
    return hasExpensiveScans;
}
```


### Task 3.2: Enhanced Plan Parsing

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/ExecutionPlanService.cs`

**Enhanced ParseExecutionPlanXml:**

```csharp
/// <summary>
/// Enhanced XML parsing with detailed warnings and recommendations
/// </summary>
private ExecutionPlan ParseExecutionPlanXml(string xml)
{
    var doc = XDocument.Parse(xml);
    var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
    
    var stmtSimple = doc.Descendants(ns + "StmtSimple").FirstOrDefault();
    if (stmtSimple == null)
        throw new InvalidOperationException("Invalid execution plan XML");
    
    var plan = new ExecutionPlan
    {
        EstimatedTotalCost = double.Parse(stmtSimple.Attribute("StatementSubTreeCost")?.Value ?? "0"),
        EstimatedRows = long.Parse(stmtSimple.Attribute("StatementEstRows")?.Value ?? "0"),
        Operators = new List<PlanOperator>(),
        Warnings = new List<string>(),
        MissingIndexes = new List<MissingIndex>() // ← NEW
    };
    
    // Extract operators (existing code)
    var relOps = doc.Descendants(ns + "RelOp");
    foreach (var relOp in relOps)
    {
        var op = ParseOperator(relOp, ns);
        plan.Operators.Add(op);
    }
    
    // ========== NEW: Extract warnings ==========
    var warnings = doc.Descendants(ns + "Warnings");
    foreach (var warning in warnings)
    {
        foreach (var child in warning.Elements())
        {
            var warningType = child.Name.LocalName;
            plan.Warnings.Add(warningType);
            
            // Extract detailed warning info
            if (warningType == "ColumnsWithNoStatistics")
            {
                var columns = child.Descendants(ns + "ColumnReference")
                    .Select(c => c.Attribute("Column")?.Value)
                    .Where(c => c != null);
                plan.Warnings.Add($"Missing stats on: {string.Join(", ", columns)}");
            }
        }
    }
    
    // ========== NEW: Extract missing index recommendations ==========
    var missingIndexes = doc.Descendants(ns + "MissingIndexes");
    foreach (var missingIndex in missingIndexes)
    {
        var indexGroup = missingIndex.Descendants(ns + "MissingIndexGroup").FirstOrDefault();
        if (indexGroup != null)
        {
            var impact = double.Parse(indexGroup.Attribute("Impact")?.Value ?? "0");
            var index = missingIndex.Descendants(ns + "MissingIndex").FirstOrDefault();
            
            if (index != null)
            {
                var recommendation = new MissingIndex
                {
                    Database = index.Attribute("Database")?.Value ?? "",
                    Schema = index.Attribute("Schema")?.Value ?? "",
                    Table = index.Attribute("Table")?.Value ?? "",
                    Impact = impact,
                    EqualityColumns = ExtractColumns(index, ns, "ColumnGroup[@Usage='EQUALITY']"),
                    InequalityColumns = ExtractColumns(index, ns, "ColumnGroup[@Usage='INEQUALITY']"),
                    IncludedColumns = ExtractColumns(index, ns, "ColumnGroup[@Usage='INCLUDE']")
                };
                
                plan.MissingIndexes.Add(recommendation);
            }
        }
    }
    
    // ========== NEW: Detect implicit conversions ==========
    var converts = doc.Descendants(ns + "ScalarOperator")
        .Where(so => so.Attribute("ScalarString")?.Value.Contains("CONVERT_IMPLICIT") == true);
    
    foreach (var convert in converts)
    {
        plan.Warnings.Add($"Implicit conversion: {convert.Attribute("ScalarString")?.Value}");
    }
    
    return plan;
}

private List<string> ExtractColumns(XElement index, XNamespace ns, string xpath)
{
    var columns = new List<string>();
    var columnGroup = index.XPathSelectElement(xpath, ns);
    
    if (columnGroup != null)
    {
        columns = columnGroup.Descendants(ns + "Column")
            .Select(c => c.Attribute("Name")?.Value)
            .Where(c => c != null)
            .ToList();
    }
    
    return columns;
}
```

### Task 3.3: New Model Classes

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/Models/ExecutionPlanModels.cs` (NEW)

```csharp
namespace TextToSqlAgent.Application.Services.QueryOptimizer.Models;

/// <summary>
/// Pre-flight analysis result
/// </summary>
public class PreFlightAnalysis
{
    public double EstimatedCost { get; set; }
    public long EstimatedRows { get; set; }
    public List<CostDriver> CostDrivers { get; set; } = new();
    public List<PlanWarning> Warnings { get; set; } = new();
    public List<IndexRecommendation> IndexRecommendations { get; set; } = new();
    public List<ImplicitConversion> ImplicitConversions { get; set; } = new();
    public List<string> MissingStatistics { get; set; } = new();
    public List<PlanOperator> ExpensiveOperators { get; set; } = new();
    public bool NeedsOptimization { get; set; }
}

public class CostDriver
{
    public string OperatorType { get; set; } = string.Empty;
    public double Cost { get; set; }
    public double Rows { get; set; }
    public string? ObjectName { get; set; }
    public string? IndexName { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PlanWarning
{
    public string RawWarning { get; set; } = string.Empty;
    public WarningType Type { get; set; }
    public WarningSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public enum WarningType
{
    MissingJoinPredicate,
    MissingStatistics,
    UnmatchedIndexes,
    SpillToTempDb,
    ImplicitConversion,
    ParameterSniffing,
    Other
}

public enum WarningSeverity
{
    Info,
    Medium,
    High,
    Critical
}

public class IndexRecommendation
{
    public string TableName { get; set; } = string.Empty;
    public List<string> KeyColumns { get; set; } = new();
    public List<string> IncludeColumns { get; set; } = new();
    public double ImpactPercentage { get; set; }
    public string CreateStatement { get; set; } = string.Empty;
}

public class ImplicitConversion
{
    public string ColumnName { get; set; } = string.Empty;
    public string FromType { get; set; } = string.Empty;
    public string ToType { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
}

public class MissingIndex
{
    public string Database { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public double Impact { get; set; }
    public List<string> EqualityColumns { get; set; } = new();
    public List<string> InequalityColumns { get; set; } = new();
    public List<string> IncludedColumns { get; set; } = new();
    
    public string GenerateCreateStatement()
    {
        var indexName = $"IX_{Table}_{string.Join("_", EqualityColumns.Concat(InequalityColumns))}";
        var keyColumns = string.Join(", ", EqualityColumns.Concat(InequalityColumns));
        
        var sql = $"CREATE NONCLUSTERED INDEX {indexName} ON {Schema}.{Table} ({keyColumns})";
        
        if (IncludedColumns.Any())
        {
            sql += $" INCLUDE ({string.Join(", ", IncludedColumns)})";
        }
        
        return sql;
    }
}
```


---

## PHASE 4: ENHANCED LLM PROMPT (Priority: HIGH)
**Timeline:** 1-2 days  
**Effort:** Low  
**Impact:** Very High

### Task 4.1: Update Prompt Template

**File:** `Prompts/QueryOptimizer/optimize-query.skprompt.txt`

**New Enhanced Prompt:**

```
SYSTEM: Bạn là chuyên gia tối ưu hóa T-SQL với kiến thức DBA senior-level.

========================================
📊 EXECUTION PLAN ANALYSIS (ORIGINAL QUERY)
========================================

Estimated Cost: {{$execution_plan_cost}}
Estimated Rows: {{$execution_plan_rows}}

🔥 TOP COST DRIVERS:
{{$cost_drivers}}

⚠️ EXECUTION PLAN WARNINGS:
{{$plan_warnings}}

💡 MISSING INDEX RECOMMENDATIONS:
{{$missing_indexes}}

⚠️ IMPLICIT CONVERSIONS DETECTED:
{{$implicit_conversions}}

========================================
🐛 ANTI-PATTERNS DETECTED (Static Analysis)
========================================

{{$detected_issues}}

========================================
📊 COLUMN STATISTICS & DATA SKEW ANALYSIS
========================================

{{$column_statistics}}

========================================
🗄️ DATABASE SCHEMA CONTEXT
========================================

{{$schema_context}}

========================================
📝 QUERY GỐC
========================================

{{$original_sql}}

========================================
🎯 NHIỆM VỤ TỐI ƯU HÓA
========================================

Phân tích và tối ưu hóa query dựa trên:

1. EXECUTION PLAN INSIGHTS (ưu tiên cao nhất):
   - Giải quyết các warnings từ execution plan
   - Tối ưu các operators có cost cao
   - Xử lý implicit conversions
   - Implement missing index recommendations

2. DATA SKEW CONSIDERATIONS:
   - Nếu SkewFactor > 0.7: Cân nhắc filtered index hoặc parameter sniffing mitigation
   - Nếu Selectivity < 0.01: Index có thể không hiệu quả
   - Sử dụng top values để đưa ra recommendations cụ thể

3. ANTI-PATTERN FIXES (theo thứ tự ưu tiên):
   a. SARGability issues (AP-02, AP-03, AP-10, AP-21) - CRITICAL
   b. Index utilization (AP-05, AP-23)
   c. JOIN optimization (AP-06, AP-08, AP-12)
   d. Code quality (AP-01, AP-07, AP-11, AP-13)

4. PERFORMANCE OPTIMIZATION:
   - Minimize table scans → index seeks
   - Reduce nested loops on large datasets → hash/merge joins
   - Eliminate N+1 patterns
   - Optimize sort operations

========================================
📤 OUTPUT FORMAT (JSON)
========================================

{
  "optimized_sql": "-- Optimized query here",
  "is_changed": true/false,
  "severity": "critical|warning|ok",
  "issues_fixed": ["AP-01", "AP-02", ...],
  "explanation": "Detailed explanation in Vietnamese",
  "estimated_improvement": "Based on execution plan: Cost reduced from X to Y (~Z% faster)",
  "index_suggestions": [
    "CREATE NONCLUSTERED INDEX IX_... -- Impact: High, improves WHERE clause performance"
  ],
  "data_skew_considerations": "Explain how data skew affects optimization decisions"
}

========================================
⚠️ CRITICAL RULES
========================================

1. ALWAYS preserve query semantics - results must be identical
2. If no optimization possible, return is_changed: false
3. Base improvement estimates on ACTUAL execution plan metrics, not guesses
4. Consider data skew when recommending indexes
5. Explain WHY each change improves performance
6. Prioritize fixes that address execution plan warnings
7. For high-skew columns, mention parameter sniffing risks
```

### Task 4.2: Update QueryOptimizerService to Use Enhanced Prompt

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`

```csharp
private async Task<OptimizationResult> OptimizeWithLLMAsync(
    string sql,
    QueryMetadata metadata,
    SchemaContext schemaContext,
    string modelName,
    CancellationToken cancellationToken)
{
    // ========== Get pre-flight analysis ==========
    var preFlightAnalysis = await _executionPlanService.GetPreFlightAnalysisAsync(
        sql,
        connectionString,
        cancellationToken);
    
    // If query is simple and has no issues, skip LLM
    if (!preFlightAnalysis.NeedsOptimization && !metadata.DetectedIssues.Any())
    {
        return new OptimizationResult
        {
            OriginalSql = sql,
            OptimizedSql = sql,
            IsChanged = false,
            Severity = "ok",
            Explanation = "Query is already optimal - no changes needed.",
            ComplexityScore = metadata.ComplexityScore
        };
    }
    
    // ========== Get column statistics ==========
    var criticalColumns = metadata.GetCriticalColumns();
    var columnStats = await GetColumnStatisticsForQuery(
        metadata.Tables,
        criticalColumns,
        connectionString,
        cancellationToken);
    
    // ========== Build prompt sections ==========
    var detectedIssuesText = BuildIssuesText(metadata.DetectedIssues);
    var schemaContextText = BuildSchemaContextText(schemaContext);
    var columnStatsText = BuildColumnStatsText(columnStats);
    var costDriversText = BuildCostDriversText(preFlightAnalysis.CostDrivers);
    var planWarningsText = BuildPlanWarningsText(preFlightAnalysis.Warnings);
    var missingIndexesText = BuildMissingIndexesText(preFlightAnalysis.IndexRecommendations);
    var implicitConversionsText = BuildImplicitConversionsText(preFlightAnalysis.ImplicitConversions);
    
    // ========== Load and populate prompt ==========
    var promptTemplate = await LoadPromptTemplate();
    var prompt = promptTemplate
        .Replace("{{$execution_plan_cost}}", preFlightAnalysis.EstimatedCost.ToString("F2"))
        .Replace("{{$execution_plan_rows}}", preFlightAnalysis.EstimatedRows.ToString("N0"))
        .Replace("{{$cost_drivers}}", costDriversText)
        .Replace("{{$plan_warnings}}", planWarningsText)
        .Replace("{{$missing_indexes}}", missingIndexesText)
        .Replace("{{$implicit_conversions}}", implicitConversionsText)
        .Replace("{{$detected_issues}}", detectedIssuesText)
        .Replace("{{$column_statistics}}", columnStatsText)
        .Replace("{{$schema_context}}", schemaContextText)
        .Replace("{{$original_sql}}", sql);
    
    // ========== Call LLM ==========
    var responseText = await _llmClient.CompleteAsync(prompt, cancellationToken);
    var cleanedResponse = CleanJsonResponse(responseText);
    var llmResponse = JsonSerializer.Deserialize<LLMOptimizationResponse>(cleanedResponse);
    
    return new OptimizationResult
    {
        OriginalSql = sql,
        OptimizedSql = llmResponse?.OptimizedSql ?? sql,
        IsChanged = llmResponse?.IsChanged ?? false,
        Severity = llmResponse?.Severity ?? "ok",
        DetectedIssues = metadata.DetectedIssues,
        IssuesFixed = llmResponse?.IssuesFixed ?? new(),
        Explanation = llmResponse?.Explanation ?? "",
        EstimatedImprovement = llmResponse?.EstimatedImprovement ?? "",
        IndexSuggestions = llmResponse?.IndexSuggestions ?? new(),
        ComplexityScore = metadata.ComplexityScore,
        ModelUsed = modelName,
        PreFlightAnalysis = preFlightAnalysis // ← NEW: Include in result
    };
}
```


### Task 4.3: Helper Methods for Prompt Building

**File:** `TextToSqlAgent.Application/Services/QueryOptimizer/QueryOptimizerService.cs`

```csharp
private string BuildCostDriversText(List<CostDriver> drivers)
{
    if (!drivers.Any())
        return "No significant cost drivers identified.";
    
    var lines = new List<string>();
    for (int i = 0; i < drivers.Count; i++)
    {
        var driver = drivers[i];
        lines.Add($"{i + 1}. {driver.Description}");
    }
    
    return string.Join("\n", lines);
}

private string BuildPlanWarningsText(List<PlanWarning> warnings)
{
    if (!warnings.Any())
        return "✅ No warnings detected in execution plan.";
    
    var lines = new List<string>();
    foreach (var warning in warnings.OrderByDescending(w => w.Severity))
    {
        var icon = warning.Severity switch
        {
            WarningSeverity.Critical => "🔴",
            WarningSeverity.High => "🟠",
            WarningSeverity.Medium => "🟡",
            _ => "ℹ️"
        };
        
        lines.Add($"{icon} [{warning.Severity}] {warning.Description}");
        lines.Add($"   Recommendation: {warning.Recommendation}");
        lines.Add("");
    }
    
    return string.Join("\n", lines);
}

private string BuildMissingIndexesText(List<IndexRecommendation> recommendations)
{
    if (!recommendations.Any())
        return "No missing index recommendations from execution plan.";
    
    var lines = new List<string>();
    foreach (var rec in recommendations.OrderByDescending(r => r.ImpactPercentage))
    {
        lines.Add($"📊 Impact: {rec.ImpactPercentage:F1}% improvement");
        lines.Add($"   Table: {rec.TableName}");
        lines.Add($"   Key Columns: {string.Join(", ", rec.KeyColumns)}");
        
        if (rec.IncludeColumns.Any())
        {
            lines.Add($"   Include Columns: {string.Join(", ", rec.IncludeColumns)}");
        }
        
        lines.Add($"   SQL: {rec.CreateStatement}");
        lines.Add("");
    }
    
    return string.Join("\n", lines);
}

private string BuildImplicitConversionsText(List<ImplicitConversion> conversions)
{
    if (!conversions.Any())
        return "✅ No implicit conversions detected.";
    
    var lines = new List<string>();
    foreach (var conv in conversions)
    {
        lines.Add($"⚠️ Column: {conv.ColumnName}");
        lines.Add($"   Converting: {conv.FromType} → {conv.ToType}");
        lines.Add($"   Impact: {conv.Impact}");
        lines.Add("");
    }
    
    return string.Join("\n", lines);
}
```

---

## PHASE 5: FRONTEND ENHANCEMENTS (Priority: MEDIUM)
**Timeline:** 2-3 days  
**Effort:** Medium  
**Impact:** High (User Experience)

### Task 5.1: Display Pre-Flight Analysis

**File:** `frontend/src/pages/QueryLab.jsx`

**New Component: PreFlightAnalysisPanel**

```jsx
const PreFlightAnalysisPanel = ({ analysis }) => {
  if (!analysis) return null;
  
  return (
    <div className="bg-gray-50 rounded-lg p-4 mb-4">
      <h3 className="text-lg font-semibold mb-3">
        📊 Execution Plan Analysis (Original Query)
      </h3>
      
      <div className="grid grid-cols-2 gap-4 mb-4">
        <div className="bg-white p-3 rounded">
          <div className="text-sm text-gray-600">Estimated Cost</div>
          <div className="text-2xl font-bold text-blue-600">
            {analysis.estimatedCost.toFixed(2)}
          </div>
        </div>
        
        <div className="bg-white p-3 rounded">
          <div className="text-sm text-gray-600">Estimated Rows</div>
          <div className="text-2xl font-bold text-green-600">
            {analysis.estimatedRows.toLocaleString()}
          </div>
        </div>
      </div>
      
      {/* Cost Drivers */}
      {analysis.costDrivers?.length > 0 && (
        <div className="mb-4">
          <h4 className="font-semibold mb-2">🔥 Top Cost Drivers</h4>
          <div className="space-y-2">
            {analysis.costDrivers.map((driver, idx) => (
              <div key={idx} className="bg-white p-3 rounded border-l-4 border-orange-500">
                <div className="font-medium">{driver.operatorType}</div>
                <div className="text-sm text-gray-600">{driver.description}</div>
                <div className="text-xs text-gray-500 mt-1">
                  Cost: {driver.cost.toFixed(2)} | Rows: {driver.rows.toLocaleString()}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
      
      {/* Warnings */}
      {analysis.warnings?.length > 0 && (
        <div className="mb-4">
          <h4 className="font-semibold mb-2">⚠️ Execution Plan Warnings</h4>
          <div className="space-y-2">
            {analysis.warnings.map((warning, idx) => (
              <div 
                key={idx} 
                className={`p-3 rounded border-l-4 ${
                  warning.severity === 'Critical' ? 'bg-red-50 border-red-500' :
                  warning.severity === 'High' ? 'bg-orange-50 border-orange-500' :
                  warning.severity === 'Medium' ? 'bg-yellow-50 border-yellow-500' :
                  'bg-blue-50 border-blue-500'
                }`}
              >
                <div className="font-medium">{warning.description}</div>
                <div className="text-sm text-gray-600 mt-1">
                  💡 {warning.recommendation}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
      
      {/* Missing Indexes */}
      {analysis.indexRecommendations?.length > 0 && (
        <div>
          <h4 className="font-semibold mb-2">💡 Missing Index Recommendations</h4>
          <div className="space-y-2">
            {analysis.indexRecommendations.map((rec, idx) => (
              <div key={idx} className="bg-white p-3 rounded border-l-4 border-green-500">
                <div className="flex justify-between items-start mb-2">
                  <div className="font-medium">{rec.tableName}</div>
                  <div className="text-sm font-semibold text-green-600">
                    Impact: {rec.impactPercentage.toFixed(1)}%
                  </div>
                </div>
                <div className="text-sm text-gray-600 mb-2">
                  Key: {rec.keyColumns.join(', ')}
                  {rec.includeColumns?.length > 0 && (
                    <> | Include: {rec.includeColumns.join(', ')}</>
                  )}
                </div>
                <code className="text-xs bg-gray-100 p-2 rounded block">
                  {rec.createStatement}
                </code>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
```

### Task 5.2: Enhanced Data Skew Visualization

**File:** `frontend/src/components/query-lab/DataSkewIndicator.jsx`

**Enhanced Component:**

```jsx
const DataSkewIndicator = ({ columnStats }) => {
  if (!columnStats || columnStats.length === 0) return null;
  
  const getSkewColor = (skewLevel) => {
    switch (skewLevel) {
      case 'Extreme': return 'bg-red-500';
      case 'High': return 'bg-orange-500';
      case 'Moderate': return 'bg-yellow-500';
      case 'Low': return 'bg-blue-500';
      default: return 'bg-green-500';
    }
  };
  
  const getSelectivityColor = (selectivity) => {
    if (selectivity > 0.5) return 'text-green-600';
    if (selectivity > 0.1) return 'text-blue-600';
    if (selectivity > 0.01) return 'text-yellow-600';
    return 'text-red-600';
  };
  
  return (
    <div className="bg-white rounded-lg shadow p-4">
      <h3 className="text-lg font-semibold mb-4">
        📊 Column Statistics & Data Skew
      </h3>
      
      <div className="space-y-4">
        {columnStats.map((stat, idx) => (
          <div key={idx} className="border rounded-lg p-4">
            <div className="flex justify-between items-start mb-3">
              <div>
                <div className="font-semibold text-lg">{stat.columnName}</div>
                <div className="text-sm text-gray-600">{stat.tableName}</div>
              </div>
              <div className={`px-3 py-1 rounded-full text-white text-sm ${getSkewColor(stat.skewLevel)}`}>
                {stat.skewLevel} Skew
              </div>
            </div>
            
            {/* Metrics Grid */}
            <div className="grid grid-cols-2 gap-3 mb-3">
              <div className="bg-gray-50 p-2 rounded">
                <div className="text-xs text-gray-600">Total Rows</div>
                <div className="text-lg font-semibold">
                  {stat.totalRows.toLocaleString()}
                </div>
              </div>
              
              <div className="bg-gray-50 p-2 rounded">
                <div className="text-xs text-gray-600">Distinct Values</div>
                <div className="text-lg font-semibold">
                  {stat.distinctValues.toLocaleString()}
                </div>
              </div>
              
              <div className="bg-gray-50 p-2 rounded">
                <div className="text-xs text-gray-600">Selectivity</div>
                <div className={`text-lg font-semibold ${getSelectivityColor(stat.selectivity)}`}>
                  {(stat.selectivity * 100).toFixed(2)}%
                </div>
              </div>
              
              <div className="bg-gray-50 p-2 rounded">
                <div className="text-xs text-gray-600">Skew Factor</div>
                <div className="text-lg font-semibold text-orange-600">
                  {(stat.skewFactor * 100).toFixed(2)}%
                </div>
              </div>
            </div>
            
            {/* Skew Visualization */}
            <div className="mb-3">
              <div className="text-sm font-medium mb-1">Data Distribution</div>
              <div className="w-full bg-gray-200 rounded-full h-4">
                <div 
                  className={`h-4 rounded-full ${getSkewColor(stat.skewLevel)}`}
                  style={{ width: `${stat.skewFactor * 100}%` }}
                />
              </div>
            </div>
            
            {/* Top Values */}
            {stat.topValues?.length > 0 && (
              <div className="mb-3">
                <div className="text-sm font-medium mb-2">Top Values</div>
                <div className="space-y-1">
                  {stat.topValues.slice(0, 5).map((tv, i) => (
                    <div key={i} className="flex justify-between text-sm">
                      <span className="text-gray-700 truncate max-w-xs">
                        '{tv.value}'
                      </span>
                      <span className="text-gray-600">
                        {tv.count.toLocaleString()} ({tv.percentage}%)
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}
            
            {/* Index Recommendation */}
            <div className="bg-blue-50 border-l-4 border-blue-500 p-3 rounded">
              <div className="text-sm font-medium text-blue-900 mb-1">
                💡 Index Recommendation
              </div>
              <div className="text-sm text-blue-800">
                {stat.indexRecommendation}
              </div>
            </div>
            
            {/* Warnings */}
            {stat.skewFactor > 0.7 && (
              <div className="bg-red-50 border-l-4 border-red-500 p-3 rounded mt-2">
                <div className="text-sm font-medium text-red-900 mb-1">
                  ⚠️ High Skew Warning
                </div>
                <div className="text-sm text-red-800">
                  Index may not be used for majority value. Consider filtered index, 
                  partitioning, or parameter sniffing mitigation (OPTION RECOMPILE, 
                  OPTIMIZE FOR UNKNOWN).
                </div>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
};

export default DataSkewIndicator;
```


---

## PHASE 6: TESTING & VALIDATION (Priority: HIGH)
**Timeline:** 2-3 days  
**Effort:** Medium  
**Impact:** Critical (Quality Assurance)

### Task 6.1: Unit Tests for New Anti-Patterns

**File:** `TextToSqlAgent.Tests.Unit/Services/QueryOptimizer/QueryMetadataVisitorTests.cs`

**New Test Cases:**

```csharp
[Fact]
public void Visit_CountStar_DetectsAP04()
{
    // Arrange
    var sql = "SELECT COUNT(*) FROM Users";
    
    // Act
    var metadata = _staticAnalyzer.AnalyzeAsync(sql).Result;
    
    // Assert
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-04");
}

[Fact]
public void Visit_OrChain_DetectsAP06()
{
    // Arrange
    var sql = "SELECT * FROM Users WHERE Status='Active' OR Status='Pending' OR Status='Approved'";
    
    // Act
    var metadata = _staticAnalyzer.AnalyzeAsync(sql).Result;
    
    // Assert
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-06");
}

[Fact]
public void Visit_Distinct_DetectsAP07()
{
    // Arrange
    var sql = "SELECT DISTINCT Name FROM Users";
    
    // Act
    var metadata = _staticAnalyzer.AnalyzeAsync(sql).Result;
    
    // Assert
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-07");
}

[Fact]
public void Visit_Union_DetectsAP08()
{
    // Arrange
    var sql = "SELECT Id FROM Users UNION SELECT Id FROM Customers";
    
    // Act
    var metadata = _staticAnalyzer.AnalyzeAsync(sql).Result;
    
    // Assert
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-08");
}

[Fact]
public void Visit_SubqueryInSelect_DetectsAP12()
{
    // Arrange
    var sql = @"
        SELECT 
            u.Name,
            (SELECT COUNT(*) FROM Orders WHERE UserId = u.Id) AS OrderCount
        FROM Users u";
    
    // Act
    var metadata = _staticAnalyzer.AnalyzeAsync(sql).Result;
    
    // Assert
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-12");
}

[Fact]
public void Visit_ImplicitConversion_DetectsAP21()
{
    // Arrange
    var sql = "SELECT * FROM Users WHERE Name = 'John'"; // Should be N'John' for nvarchar
    
    // Act
    var metadata = _staticAnalyzer.AnalyzeAsync(sql).Result;
    
    // Assert
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-21");
}
```

### Task 6.2: Integration Tests for Column Statistics

**File:** `TextToSqlAgent.Tests.Integration/Services/ColumnStatisticsServiceTests.cs` (NEW)

```csharp
public class ColumnStatisticsServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly ColumnStatisticsService _service;
    private readonly string _connectionString;
    
    [Fact]
    public async Task GetColumnStatistics_ReturnsCorrectSkewFactor()
    {
        // Arrange
        var tableName = "TestUsers";
        var columnName = "Status";
        
        // Act
        var stats = await _service.GetColumnStatisticsAsync(
            tableName, columnName, _connectionString);
        
        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.SkewFactor >= 0 && stats.SkewFactor <= 1);
        Assert.True(stats.Selectivity >= 0 && stats.Selectivity <= 1);
        Assert.NotEmpty(stats.TopValues);
    }
    
    [Fact]
    public async Task GetTableStatistics_ReturnsMultipleColumns()
    {
        // Arrange
        var tableName = "TestUsers";
        var columns = new List<string> { "Status", "Country", "Age" };
        
        // Act
        var stats = await _service.GetTableStatisticsAsync(
            tableName, columns, _connectionString);
        
        // Assert
        Assert.Equal(3, stats.Count);
        Assert.All(stats.Values, s => Assert.NotNull(s.IndexRecommendation));
    }
}
```

### Task 6.3: End-to-End Tests

**File:** `TextToSqlAgent.Tests.Integration/API/QueryOptimizerE2ETests.cs` (NEW)

```csharp
public class QueryOptimizerE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task OptimizeQuery_WithHighSkew_ReturnsSkewWarning()
    {
        // Arrange
        var sql = "SELECT * FROM Users WHERE Status = 'Active'"; // Assume 90% are Active
        
        // Act
        var result = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", new
        {
            sql = sql,
            connectionString = _testConnectionString
        });
        
        // Assert
        var response = await result.Content.ReadFromJsonAsync<OptimizationResult>();
        Assert.NotNull(response.PreFlightAnalysis);
        Assert.Contains(response.Explanation, "skew");
    }
    
    [Fact]
    public async Task OptimizeQuery_WithMissingIndex_ReturnsIndexRecommendation()
    {
        // Arrange
        var sql = "SELECT * FROM Orders WHERE CustomerId = 123 AND OrderDate > '2024-01-01'";
        
        // Act
        var result = await _client.PostAsJsonAsync("/api/query-optimizer/optimize", new
        {
            sql = sql,
            connectionString = _testConnectionString
        });
        
        // Assert
        var response = await result.Content.ReadFromJsonAsync<OptimizationResult>();
        Assert.NotEmpty(response.IndexSuggestions);
    }
}
```

### Task 6.4: Test Queries Document

**File:** `docs/testing/QUERY_OPTIMIZER_COMPREHENSIVE_TEST_QUERIES.md` (NEW)

```markdown
# Query Optimizer - Comprehensive Test Queries

## Anti-Pattern Tests

### AP-04: COUNT(*) vs COUNT(pk)
```sql
-- Should detect AP-04
SELECT COUNT(*) FROM Users;

-- Should suggest
SELECT COUNT(Id) FROM Users;
```

### AP-06: OR → IN Conversion
```sql
-- Should detect AP-06
SELECT * FROM Users 
WHERE Status='Active' OR Status='Pending' OR Status='Approved';

-- Should suggest
SELECT * FROM Users 
WHERE Status IN ('Active', 'Pending', 'Approved');
```

### AP-12: N+1 Query Pattern
```sql
-- Should detect AP-12 (Critical)
SELECT 
    u.Name,
    (SELECT COUNT(*) FROM Orders WHERE UserId = u.Id) AS OrderCount,
    (SELECT SUM(Amount) FROM Orders WHERE UserId = u.Id) AS TotalAmount
FROM Users u;

-- Should suggest JOIN instead
SELECT 
    u.Name,
    COUNT(o.Id) AS OrderCount,
    SUM(o.Amount) AS TotalAmount
FROM Users u
LEFT JOIN Orders o ON u.Id = o.UserId
GROUP BY u.Id, u.Name;
```

## Data Skew Tests

### High Skew Column
```sql
-- Assume Status column: 90% 'Active', 10% others
SELECT * FROM Users WHERE Status = 'Active';

-- Expected: Warning about parameter sniffing, filtered index suggestion
```

### Low Selectivity Column
```sql
-- Assume Gender column: only 'M', 'F', 'Other'
SELECT * FROM Users WHERE Gender = 'M';

-- Expected: Index not recommended due to low selectivity
```

## Execution Plan Tests

### Missing Index
```sql
-- No index on (CustomerId, OrderDate)
SELECT * FROM Orders 
WHERE CustomerId = 123 
  AND OrderDate > '2024-01-01';

-- Expected: Missing index recommendation with CREATE INDEX statement
```

### Implicit Conversion
```sql
-- Assume PhoneNumber is nvarchar(20)
SELECT * FROM Users WHERE PhoneNumber = '1234567890'; -- varchar literal

-- Expected: AP-21 warning, suggest N'1234567890'
```

### Table Scan
```sql
-- Large table without WHERE clause
SELECT * FROM Orders; -- Assume 1M+ rows

-- Expected: AP-23 warning, suggest adding WHERE clause
```

## Complex Query Tests

### Multi-Issue Query
```sql
-- Multiple anti-patterns
SELECT *  -- AP-01
FROM Users  -- AP-13 (missing schema)
WHERE YEAR(CreatedDate) = 2024  -- AP-02 (non-SARGable)
  AND Status='Active' OR Status='Pending'  -- AP-06 (OR chain)
  AND Name = 'John';  -- AP-21 (potential implicit conversion)

-- Expected: Detect all 4 issues, prioritize by severity
```
```


---

## 📅 IMPLEMENTATION TIMELINE

### Week 1: Foundation (Phases 1-2)
**Days 1-3: Enhanced Static Analyzer**
- [ ] Implement AP-04 through AP-25 detection
- [ ] Add helper methods for column extraction
- [ ] Create AutoFixer service
- [ ] Write unit tests for new patterns

**Days 4-5: Column Statistics Integration**
- [ ] Integrate ColumnStatisticsService into pipeline
- [ ] Build column stats text for prompt
- [ ] Test with high-skew and low-selectivity data

### Week 2: Intelligence Layer (Phases 3-4)
**Days 1-3: Execution Plan Enhancement**
- [ ] Implement PreFlightAnalysis
- [ ] Enhanced XML parsing with warnings
- [ ] Extract missing index recommendations
- [ ] Detect implicit conversions from plan

**Days 4-5: Enhanced LLM Prompt**
- [ ] Update prompt template
- [ ] Integrate all data sources into prompt
- [ ] Test LLM responses with new context
- [ ] Validate JSON parsing

### Week 3: UI & Testing (Phases 5-6)
**Days 1-2: Frontend Enhancements**
- [ ] PreFlightAnalysisPanel component
- [ ] Enhanced DataSkewIndicator
- [ ] Cost comparison visualization
- [ ] Warning badges and alerts

**Days 3-5: Comprehensive Testing**
- [ ] Unit tests for all new patterns
- [ ] Integration tests for statistics
- [ ] E2E tests for full pipeline
- [ ] Performance benchmarking
- [ ] Documentation updates

---

## 📊 SUCCESS METRICS

### Quantitative Metrics
| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Anti-patterns detected | 5 | 25+ | Pattern coverage |
| Detection accuracy | ~60% | >90% | False positive rate |
| LLM context quality | Low | High | Data-driven insights |
| Optimization success rate | ~40% | >80% | Actual improvements |
| Average response time | 3-5s | <4s | End-to-end latency |

### Qualitative Metrics
- [ ] LLM receives execution plan metrics (not just schema)
- [ ] Column statistics integrated into recommendations
- [ ] Auto-fix capability for simple patterns
- [ ] Data skew warnings visible to users
- [ ] Missing index recommendations with CREATE statements
- [ ] Implicit conversion detection and fixes

---

## 🎯 EXPECTED OUTCOMES

### Before Refactor
```
Input: SELECT * FROM Users WHERE YEAR(CreatedDate) = 2024

Detection:
- AP-01: SELECT *
- AP-02: Function on indexed column

LLM Context:
- Schema: Users table structure
- Issues: Generic descriptions

Output:
- Generic optimization suggestions
- No execution plan insights
- No data skew considerations
```

### After Refactor
```
Input: SELECT * FROM Users WHERE YEAR(CreatedDate) = 2024

Detection:
- AP-01: SELECT * (Auto-fixable)
- AP-02: Non-SARGable function (Critical)
- AP-13: Missing schema prefix (Auto-fixable)
- AP-21: Potential implicit conversion

Pre-Flight Analysis:
- Estimated Cost: 45.2
- Cost Driver: Clustered Index Scan (Cost: 42.1)
- Warning: Missing statistics on CreatedDate
- Missing Index: IX_Users_CreatedDate

Column Statistics:
- CreatedDate: 1M rows, 365 distinct values
- Selectivity: 0.0003 (Low)
- Skew: 15% in 2024, 85% historical
- Recommendation: Filtered index for recent dates

LLM Context:
✅ Execution plan metrics
✅ Column statistics with skew
✅ Missing index recommendations
✅ Implicit conversion warnings
✅ Data-driven insights

Output:
- Optimized SQL with explicit columns
- SARGable WHERE clause: CreatedDate >= '2024-01-01' AND CreatedDate < '2025-01-01'
- Schema prefix added
- Index recommendation: CREATE NONCLUSTERED INDEX IX_Users_CreatedDate_Recent 
  ON dbo.Users(CreatedDate) WHERE CreatedDate >= '2024-01-01'
- Explanation: "Cost reduced from 45.2 to 0.8 (~56x faster)"
```

---

## 🚨 RISKS & MITIGATION

### Risk 1: Performance Impact
**Risk:** Column statistics queries may slow down optimization
**Mitigation:** 
- Cache statistics for 24 hours
- Only query critical columns (WHERE/JOIN)
- Parallel statistics gathering
- Timeout after 5 seconds

### Risk 2: LLM Prompt Too Long
**Risk:** Enhanced prompt may exceed token limits
**Mitigation:**
- Prioritize most important data
- Truncate top values to top 5
- Summarize execution plan operators
- Use GPT-4 with 128K context window

### Risk 3: False Positives
**Risk:** New patterns may trigger false alarms
**Mitigation:**
- Severity levels (Info, Warning, Critical)
- User feedback mechanism
- Pattern refinement based on feedback
- Whitelist for known safe patterns

### Risk 4: Breaking Changes
**Risk:** Refactor may break existing functionality
**Mitigation:**
- Comprehensive test coverage
- Feature flags for gradual rollout
- Backward compatibility layer
- Rollback plan

---

## 📚 DOCUMENTATION UPDATES

### Files to Update
1. **README.md** - Add new features section
2. **docs/architecture/QUERY_OPTIMIZER_ARCHITECTURE.md** - Update flow diagrams
3. **docs/testing/QUERY_OPTIMIZER_TEST_PLAN.md** - Add new test cases
4. **API Documentation** - Update response schemas
5. **User Guide** - Explain new UI components

### New Documentation
1. **ANTI_PATTERNS_REFERENCE.md** - Complete list of 25+ patterns
2. **COLUMN_STATISTICS_GUIDE.md** - How to interpret skew/selectivity
3. **EXECUTION_PLAN_ANALYSIS.md** - Understanding plan warnings
4. **OPTIMIZATION_BEST_PRACTICES.md** - DBA-level recommendations

---

## 🔄 ROLLOUT STRATEGY

### Phase 1: Internal Testing (Week 1)
- Deploy to dev environment
- Test with synthetic queries
- Validate all 25+ patterns
- Performance benchmarking

### Phase 2: Beta Testing (Week 2)
- Deploy to staging
- Invite power users
- Collect feedback
- Fix critical issues

### Phase 3: Gradual Rollout (Week 3)
- Feature flag: 10% of users
- Monitor metrics
- Increase to 50% if stable
- Full rollout if no issues

### Phase 4: Post-Launch (Week 4)
- Monitor error rates
- Collect user feedback
- Iterate on patterns
- Performance tuning

---

## 💡 FUTURE ENHANCEMENTS (Post-MVP)

### Phase 7: ML-Assisted Optimization
- Train model on historical query performance
- Predict optimization success rate
- Auto-tune based on workload patterns
- Anomaly detection for query regressions

### Phase 8: Query Store Integration
- Integrate with SQL Server Query Store
- Historical performance tracking
- Regression detection
- Plan forcing recommendations

### Phase 9: Multi-Database Support
- PostgreSQL execution plan analysis
- MySQL EXPLAIN parsing
- Oracle execution plan support
- Database-specific anti-patterns

### Phase 10: Real-Time Monitoring
- Live query performance dashboard
- Alert on slow queries
- Automatic optimization suggestions
- Performance trend analysis

---

## 📞 SUPPORT & MAINTENANCE

### Monitoring
- Track optimization success rate
- Monitor LLM response times
- Alert on high error rates
- Log pattern detection accuracy

### Maintenance Tasks
- Update statistics cache weekly
- Review false positive reports
- Refine pattern detection rules
- Update prompt templates based on feedback

### Escalation Path
1. User reports issue → GitHub issue
2. Triage by severity
3. Fix in next sprint
4. Deploy with feature flag
5. Monitor and validate

---

## ✅ DEFINITION OF DONE

### Phase 1 Complete When:
- [ ] 25+ anti-patterns detected
- [ ] All unit tests passing
- [ ] Auto-fix working for simple patterns
- [ ] Code review approved

### Phase 2 Complete When:
- [ ] Column statistics integrated
- [ ] Skew/selectivity in LLM prompt
- [ ] Integration tests passing
- [ ] Performance acceptable (<5s)

### Phase 3 Complete When:
- [ ] Pre-flight analysis working
- [ ] Execution plan warnings parsed
- [ ] Missing indexes extracted
- [ ] Implicit conversions detected

### Phase 4 Complete When:
- [ ] Enhanced prompt deployed
- [ ] LLM responses validated
- [ ] JSON parsing robust
- [ ] Error handling complete

### Phase 5 Complete When:
- [ ] UI components implemented
- [ ] Data skew visualized
- [ ] Cost comparison shown
- [ ] User testing positive

### Phase 6 Complete When:
- [ ] All tests passing (>90% coverage)
- [ ] E2E tests validated
- [ ] Performance benchmarks met
- [ ] Documentation complete

---

## 🎓 LEARNING RESOURCES

### SQL Server Optimization
- [SQL Server Execution Plans](https://www.sqlshack.com/execution-plans-in-sql-server/) - Content rephrased for compliance
- [Parameter Sniffing Deep Dive](https://www.brentozar.com/archive/2013/06/the-elephant-and-the-mouse-or-parameter-sniffing-in-sql-server/) - Content rephrased for compliance
- [Implicit Conversions Guide](https://www.sqlskills.com/blogs/kimberly/implicit-conversions/) - Content rephrased for compliance

### Anti-Patterns
- SQL Anti-Patterns Book by Bill Karwin
- High Performance SQL Server by Benjamin Nevarez
- SQL Server Query Performance Tuning by Grant Fritchey

### Tools
- SQL Server Management Studio (SSMS)
- SQL Sentry Plan Explorer
- sp_BlitzCache by Brent Ozar

---

## 📝 NOTES

### Design Decisions
1. **Why not use ML for pattern detection?**
   - Static analysis is faster and more reliable
   - ML would require training data
   - Rule-based detection is explainable

2. **Why cache column statistics for 24 hours?**
   - Data distribution changes slowly
   - Reduces database load
   - Acceptable staleness for optimization

3. **Why pre-flight analysis before LLM?**
   - Provides data-driven context
   - Reduces LLM guessing
   - Enables early exit for simple queries

4. **Why auto-fix only simple patterns?**
   - Complex patterns need semantic understanding
   - Risk of breaking query semantics
   - LLM better for complex rewrites

---

## 🏁 CONCLUSION

Kế hoạch này chuyển Query Optimizer từ "passive analyzer" thành "intelligent optimization engine" với:

✅ **25+ anti-pattern detection** (từ 5 hiện tại)  
✅ **Data-driven LLM context** (execution plan + column statistics)  
✅ **Auto-fix capability** cho patterns đơn giản  
✅ **Pre-flight analysis** để đánh giá trước khi optimize  
✅ **Enhanced UI** với data skew visualization  

**Expected Impact:**
- Detection accuracy: 60% → 90%+
- Optimization success: 40% → 80%+
- User satisfaction: Significant improvement
- DBA-level insights: Professional quality

**Timeline:** 3 weeks (15 working days)  
**Effort:** ~120 hours  
**Priority:** CRITICAL - Core feature enhancement

---

**Prepared by:** Kiro AI Assistant  
**Date:** 2026-04-10  
**Version:** 1.0  
**Status:** Ready for Implementation
```



---

## 🔴 SENIOR ARCHITECT REVIEW - CRITICAL GAPS & FIXES

### Gap 1: SQL Server 2022 Native Anti-Pattern Detection
**Issue:** Plan đang reinvent the wheel - SQL Server 2022 đã có native anti-pattern detection

**Solution:** Thêm Layer 0.5
```csharp
public class NativeAntiPatternDetector
{
    public async Task<List<NativeAntiPattern>> GetNativeAntiPatternsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        // Query Extended Events
        var sql = @"
            SELECT map_value AS AntiPatternType 
            FROM sys.dm_xe_map_values  
            WHERE name = N'query_antipattern_type'";
        
        // Also check Query Store if enabled
        var queryStoreEnabled = await CheckQueryStoreStatusAsync(connectionString);
        if (queryStoreEnabled)
        {
            // Get historical anti-patterns from Query Store
        }
    }
}
```

**References:**
- [SQL Server Anti-Pattern Detection](https://learn.microsoft.com/en-us/sql/relational-databases/extended-events/use-the-system-health-session)
- [Query Store Integration](https://learn.microsoft.com/en-us/sql/relational-databases/performance/monitoring-performance-by-using-the-query-store)

### Gap 2: Parameter Sensitive Plan (PSP) Optimization - SQL Server 2022
**Issue:** AP-19 đề xuất OPTION RECOMPILE là outdated advice cho SQL Server 2022

**SQL Server 2022 PSP:**
- Tự động cache 3 execution plans per query (Low, Medium, High cardinality)
- Chọn plan tại runtime dựa trên parameter value
- Enabled khi compatibility level >= 160

**Solution:** Check compatibility level trước khi recommend
```csharp
public async Task<int> GetCompatibilityLevelAsync(string connectionString)
{
    var sql = "SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()";
    // Return compatibility level
}

// In AP-19 detection
if (compatibilityLevel >= 160)
{
    recommendation = "SQL Server 2022 PSP handles this automatically. Check PSP status with sys.query_store_plan_feedback.";
}
else
{
    recommendation = "Consider OPTION RECOMPILE or OPTIMIZE FOR UNKNOWN";
}
```

**References:**
- [Parameter Sensitive Plan Optimization](https://learn.microsoft.com/en-us/sql/relational-databases/performance/parameter-sensitive-plan-optimization)
- [PSP Deep Dive](https://www.sqlauthority.com/2022/11/16/sql-server-parameter-sensitive-plan-optimization/)

### Gap 3: AP-18 Missing Keyset Pagination Recommendation
**Issue:** Plan chỉ đề xuất OFFSET/FETCH, thiếu pattern tốt nhất

**Solution:** 3-tier recommendation
```csharp
public string GetPaginationRecommendation(long totalRows, int pageSize, int pageNumber)
{
    if (totalRows < 10000)
    {
        return "OFFSET/FETCH is acceptable for small datasets";
    }
    else if (pageNumber * pageSize > 1000)
    {
        return @"
            Keyset Pagination recommended for large offsets:
            
            -- Instead of: OFFSET 10000 ROWS FETCH NEXT 20 ROWS ONLY
            -- Use: WHERE Id > @LastSeenId ORDER BY Id FETCH NEXT 20 ROWS ONLY
            
            Benefits: O(1) performance regardless of page number";
    }
    else
    {
        return "OFFSET/FETCH acceptable, but monitor performance for large offsets";
    }
}
```

**Reference:** [Keyset Pagination Guide](https://use-the-index-luke.com/no-offset)

### Gap 4: AutoFixer Semantic Validation - CRITICAL SAFETY ISSUE
**Issue:** Auto-fix có thể gây silent semantic breakage

**Risks:**
- SELECT * expansion: Column order changes break ordinal-dependent code
- OR → IN conversion: Nullable columns với NULL comparison có semantics khác
- Schema prefix: Computed columns có thể thay đổi behavior

**Solution:** Semantic validation layer
```csharp
public class AutoFixResult
{
    public string FixedSql { get; set; }
    public bool RequiresSemanticValidation { get; set; }
    public string ValidationQuery { get; set; }
    public ConfidenceLevel Confidence { get; set; } // High/Medium/Low
    public List<string> SemanticRisks { get; set; }
}

public class AutoFixer
{
    public AutoFixResult FixSelectStar(string sql, SchemaContext schema)
    {
        var fixed = ExpandSelectStar(sql, schema);
        
        return new AutoFixResult
        {
            FixedSql = fixed,
            RequiresSemanticValidation = true,
            ValidationQuery = GenerateValidationQuery(sql, fixed),
            Confidence = ConfidenceLevel.Medium,
            SemanticRisks = new List<string>
            {
                "Column order may differ if schema has computed columns",
                "Result set structure changes may break downstream code"
            }
        };
    }
    
    private string GenerateValidationQuery(string original, string fixed)
    {
        // Generate query to verify results are identical
        return $@"
            WITH Original AS ({original}),
                 Fixed AS ({fixed})
            SELECT 
                CASE WHEN EXISTS (
                    SELECT * FROM Original EXCEPT SELECT * FROM Fixed
                    UNION ALL
                    SELECT * FROM Fixed EXCEPT SELECT * FROM Original
                ) THEN 'DIFFERENT' ELSE 'IDENTICAL' END AS ValidationResult";
    }
}
```

### Gap 5: Column Statistics Cache Invalidation
**Issue:** 24h TTL không đủ - DDL changes làm stats stale ngay lập tức

**Solution:** DDL-aware cache invalidation
```csharp
public class ColumnStatisticsService
{
    public async Task<ColumnStatistics?> GetColumnStatisticsAsync(
        string tableName,
        string columnName,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        // Get stats last updated time from SQL Server
        var statsLastUpdated = await GetStatsLastUpdatedAsync(
            tableName, columnName, connectionString);
        
        // Include stats timestamp in cache key
        var cacheKey = $"stats:{tableName}:{columnName}:{statsLastUpdated:yyyyMMddHHmm}";
        
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<ColumnStatistics>(cached);
        }
        
        // Query fresh statistics
        var stats = await QueryColumnStatisticsAsync(
            tableName, columnName, connectionString, cancellationToken);
        
        // Cache with stats-aware key
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(stats), 
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            }, cancellationToken);
        
        return stats;
    }
    
    private async Task<DateTime> GetStatsLastUpdatedAsync(
        string tableName, string columnName, string connectionString)
    {
        var sql = @"
            SELECT sp.last_updated
            FROM sys.stats s
            CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
            WHERE OBJECT_NAME(s.object_id) = @TableName
              AND EXISTS (
                  SELECT 1 FROM sys.stats_columns sc
                  JOIN sys.columns c ON sc.object_id = c.object_id AND sc.column_id = c.column_id
                  WHERE sc.stats_id = s.stats_id AND c.name = @ColumnName
              )";
        
        // Execute and return last_updated
    }
}
```

### Gap 6: LLM Token Budget Management
**Issue:** Enhanced prompt có thể exceed token limits

**Solution:** Priority-based truncation
```csharp
public class ContextBudgetManager
{
    private const int MaxContextTokens = 6000;
    private const int TokensPerChar = 4; // Rough estimate
    
    public string BuildPrioritizedContext(
        PreFlightAnalysis preFlight,
        Dictionary<string, ColumnStatistics> columnStats,
        List<AntiPattern> issues,
        SchemaContext schema)
    {
        var sections = new List<ContextSection>
        {
            new() { Priority = 1, Name = "Critical Warnings", 
                   Content = BuildWarningsText(preFlight.Warnings.Where(w => w.Severity == WarningSeverity.Critical)) },
            new() { Priority = 2, Name = "Cost Drivers", 
                   Content = BuildCostDriversText(preFlight.CostDrivers) },
            new() { Priority = 3, Name = "Critical Issues", 
                   Content = BuildIssuesText(issues.Where(i => i.Severity == Severity.Critical)) },
            new() { Priority = 4, Name = "High Skew Columns", 
                   Content = BuildColumnStatsText(columnStats.Where(kvp => kvp.Value.SkewFactor > 0.7).ToDictionary()) },
            new() { Priority = 5, Name = "Other Warnings", 
                   Content = BuildWarningsText(preFlight.Warnings.Where(w => w.Severity != WarningSeverity.Critical)) },
            new() { Priority = 6, Name = "All Column Stats", 
                   Content = BuildColumnStatsText(columnStats) },
            new() { Priority = 7, Name = "Schema Context", 
                   Content = BuildSchemaContextText(schema) }
        };
        
        var result = new StringBuilder();
        var currentTokens = 0;
        
        foreach (var section in sections.OrderBy(s => s.Priority))
        {
            var sectionTokens = EstimateTokens(section.Content);
            
            if (currentTokens + sectionTokens > MaxContextTokens)
            {
                // Truncate or skip lower priority sections
                if (section.Priority <= 4) // Critical sections
                {
                    // Truncate but include
                    var truncated = TruncateToFit(section.Content, MaxContextTokens - currentTokens);
                    result.AppendLine($"## {section.Name} (TRUNCATED)");
                    result.AppendLine(truncated);
                }
                break;
            }
            
            result.AppendLine($"## {section.Name}");
            result.AppendLine(section.Content);
            result.AppendLine();
            currentTokens += sectionTokens;
        }
        
        return result.ToString();
    }
    
    private int EstimateTokens(string text)
    {
        return text.Length / TokensPerChar;
    }
}

public class ContextSection
{
    public int Priority { get; set; }
    public string Name { get; set; }
    public string Content { get; set; }
}
```

---

## 🟡 ARCHITECTURAL TRADE-OFFS & DECISIONS

### Trade-off 1: Estimated Plan vs Actual Plan
**Decision:** Use estimated execution plan

**Pros:**
- Production-safe (no query execution)
- Fast (<100ms)
- No data modification risk

**Cons:**
- Estimated vs actual row count discrepancies
- Stale statistics mislead estimates
- No runtime-specific issues detected

**Mitigation:**
```csharp
public class ExecutionPlanService
{
    public async Task<PreFlightAnalysis> GetPreFlightAnalysisAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var plan = await GetEstimatedPlanAsync(sql, connectionString, cancellationToken);
        
        // Check statistics freshness
        var staleStats = await DetectStaleStatisticsAsync(plan, connectionString);
        
        if (staleStats.Any())
        {
            plan.Warnings.Add(new PlanWarning
            {
                Type = WarningType.StaleStatistics,
                Severity = WarningSeverity.High,
                Description = $"Statistics are stale (last updated > 7 days): {string.Join(", ", staleStats)}",
                Recommendation = "Run UPDATE STATISTICS before trusting estimated plan metrics"
            });
        }
        
        return analysis;
    }
    
    private async Task<List<string>> DetectStaleStatisticsAsync(
        ExecutionPlan plan, string connectionString)
    {
        var sql = @"
            SELECT 
                OBJECT_NAME(s.object_id) + '.' + c.name AS ColumnName,
                sp.last_updated,
                sp.modification_counter,
                sp.rows
            FROM sys.stats s
            CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
            JOIN sys.stats_columns sc ON s.object_id = sc.object_id AND s.stats_id = sc.stats_id
            JOIN sys.columns c ON sc.object_id = c.object_id AND sc.column_id = c.column_id
            WHERE sp.last_updated < DATEADD(DAY, -7, GETDATE())
               OR sp.modification_counter > sp.rows * 0.2";
        
        // Return list of stale statistics
    }
}
```

### Trade-off 2: False Positive Rate in Anti-Patterns
**Issue:** AP-07 (DISTINCT), AP-08 (UNION), AP-23 (Missing WHERE) có false positive rate cao

**Decision:** Implement AntiPatternContext for suppression

```csharp
public class AntiPatternContext
{
    public bool IsAnalyticalQuery { get; set; }  // Suppress AP-23
    public bool HasUniqueConstraints { get; set; }  // Suppress AP-07
    public QueryIntent Intent { get; set; }  // QUERY vs WRITE vs DDL
    public bool IsReportingQuery { get; set; }  // Suppress AP-08
}

public class QueryMetadataVisitor : TSqlFragmentVisitor
{
    private AntiPatternContext _context;
    
    public override void Visit(SelectStatement node)
    {
        // Infer context from query structure
        _context = InferContext(node);
        
        if (node.QueryExpression is QuerySpecification spec)
        {
            if (spec.UniqueRowFilter == UniqueRowFilter.Distinct)
            {
                // Only warn if NOT analytical query
                if (!_context.IsAnalyticalQuery && !_context.HasUniqueConstraints)
                {
                    DetectedIssues.Add(new AntiPattern
                    {
                        Code = "AP-07",
                        Severity = Severity.Info, // Downgraded from Warning
                        Title = "DISTINCT usage detected",
                        Description = "Verify if DISTINCT is necessary. May hide data quality issues.",
                        Impact = "Additional sorting/hashing overhead"
                    });
                }
            }
        }
        
        base.Visit(node);
    }
    
    private AntiPatternContext InferContext(SelectStatement node)
    {
        var context = new AntiPatternContext();
        
        // Analytical query indicators
        if (HasAggregates(node) || HasWindowFunctions(node) || HasGroupBy(node))
        {
            context.IsAnalyticalQuery = true;
        }
        
        // Reporting query indicators
        if (HasUnion(node) && HasAggregates(node))
        {
            context.IsReportingQuery = true;
        }
        
        return context;
    }
}
```

### Trade-off 3: Execution Plan Retrieval Side Effects
**Issue:** `SET SHOWPLAN_XML ON` requires VIEW DATABASE STATE permission

**Decision:** Permission check + graceful degradation

```csharp
public class ExecutionPlanService
{
    public async Task<bool> CanGetExecutionPlanAsync(string connectionString)
    {
        try
        {
            var sql = @"
                SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') AS HasPermission";
            
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            using var command = new SqlCommand(sql, connection);
            var hasPermission = (int)await command.ExecuteScalarAsync();
            
            return hasPermission == 1;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<PreFlightAnalysis> GetPreFlightAnalysisAsync(
        string sql,
        string connectionString,
        CancellationToken cancellationToken)
    {
        // Check permission first
        if (!await CanGetExecutionPlanAsync(connectionString))
        {
            _logger.LogWarning("VIEW DATABASE STATE permission not available. Falling back to static analysis only.");
            
            return new PreFlightAnalysis
            {
                NeedsOptimization = true,
                Warnings = new List<PlanWarning>
                {
                    new()
                    {
                        Type = WarningType.Other,
                        Severity = WarningSeverity.Info,
                        Description = "Execution plan analysis unavailable (missing VIEW DATABASE STATE permission)",
                        Recommendation = "Grant VIEW DATABASE STATE for detailed plan analysis"
                    }
                }
            };
        }
        
        // Proceed with plan retrieval
        var plan = await GetEstimatedPlanAsync(sql, connectionString, cancellationToken);
        // ... rest of analysis
    }
}
```

---

## 💡 ALTERNATIVE APPROACH - QUERY STORE INTEGRATION

### Why Query Store?
Query Store cho phép track query performance history, detect regressions, và force specific plans — và Query Store enabled by default trong SQL Server 2022.

**Benefits:**
- Actual execution history thay vì estimates
- Historical performance data
- Regression detection
- Plan forcing capabilities

### Implementation
```csharp
public class QueryStoreService
{
    public async Task<QueryStoreMetrics?> GetHistoricalMetricsAsync(
        string queryHash,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT TOP 1 
                qsp.query_plan,
                qsrs.avg_duration,
                qsrs.avg_logical_io_reads,
                qsrs.avg_physical_io_reads,
                qsrs.avg_cpu_time,
                qsrs.last_execution_time
            FROM sys.query_store_query_stats qsrs
            JOIN sys.query_store_plan qsp ON qsrs.plan_id = qsp.plan_id
            JOIN sys.query_store_query qsq ON qsp.query_id = qsq.query_id
            WHERE qsq.query_hash = @queryHash
            ORDER BY qsrs.last_execution_time DESC";
        
        // Execute and return metrics
    }
    
    public async Task<bool> IsQueryStoreEnabledAsync(string connectionString)
    {
        var sql = @"
            SELECT is_query_store_on 
            FROM sys.databases 
            WHERE name = DB_NAME()";
        
        // Check if Query Store is enabled
    }
}
```

**Note:** Đây nên là Phase 7 (Post-MVP) nhưng cần được mention trong architecture docs ngay bây giờ để không bị close off bởi các design decisions hiện tại.

---

## 📋 UPDATED IMPLEMENTATION PRIORITIES

### MUST FIX BEFORE IMPLEMENTATION:
1. ✅ AutoFixer cần semantic validation - KHÔNG ship mà không có
2. ✅ Column statistics cache cần DDL invalidation hook
3. ✅ Execution plan retrieval cần permission check + graceful degradation
4. ✅ Token budget management trong Phase 4

### SHOULD UPDATE IN DOCUMENT:
5. ✅ AP-19 → Acknowledge PSP Optimization của SQL Server 2022
6. ✅ AP-18 → Thêm Keyset Pagination recommendation
7. ✅ False positive suppression với AntiPatternContext
8. ✅ Statistics freshness check

### NICE-TO-HAVE FOR ROADMAP:
9. SQL Server 2022 native XE anti-pattern events integration (Phase 0)
10. Query Store integration (Phase 7)

---

## 🎯 REVISED SUCCESS METRICS

### Quantitative Metrics (Updated)
| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Anti-patterns detected | 5 | 25+ | Pattern coverage |
| Detection accuracy | ~60% | >90% | False positive rate <10% |
| LLM context quality | Low | High | Data-driven insights |
| Optimization success rate | ~40% | >80% | Actual improvements |
| Average response time | 3-5s | <4s | End-to-end latency |
| Semantic validation coverage | 0% | 100% | All auto-fixes validated |
| Permission handling | None | Graceful | No crashes on missing perms |

### Qualitative Metrics (Updated)
- [x] LLM receives execution plan metrics (not just schema)
- [x] Column statistics integrated into recommendations
- [x] Auto-fix capability with semantic validation
- [x] Data skew warnings visible to users
- [x] Missing index recommendations with CREATE statements
- [x] Implicit conversion detection and fixes
- [x] SQL Server 2022 PSP awareness
- [x] Keyset pagination recommendations
- [x] False positive suppression
- [x] Token budget management

---

## 🚨 UPDATED RISKS & MITIGATION

### Risk 1: Performance Impact
**Risk:** Column statistics queries may slow down optimization
**Mitigation:** 
- Cache statistics with DDL-aware invalidation
- Only query critical columns (WHERE/JOIN)
- Parallel statistics gathering
- Timeout after 5 seconds

### Risk 2: LLM Prompt Too Long
**Risk:** Enhanced prompt may exceed token limits
**Mitigation:** 
- ContextBudgetManager with priority truncation
- Critical sections always included
- Lower priority sections truncated first
- Use GPT-4 with 128K context window

### Risk 3: False Positives
**Risk:** New patterns may trigger false alarms
**Mitigation:**
- AntiPatternContext for intelligent suppression
- Severity levels (Info, Warning, Critical)
- User feedback mechanism
- Pattern refinement based on feedback
- Whitelist for known safe patterns

### Risk 4: Breaking Changes
**Risk:** Refactor may break existing functionality
**Mitigation:**
- Comprehensive test coverage
- Feature flags for gradual rollout
- Backward compatibility layer
- Rollback plan

### Risk 5: Semantic Breakage from Auto-Fix (NEW)
**Risk:** Auto-fix changes query semantics silently
**Mitigation:**
- Semantic validation for all auto-fixes
- Confidence levels (High/Medium/Low)
- Validation query generation
- User confirmation for Medium/Low confidence
- Detailed semantic risk warnings

### Risk 6: Permission Issues (NEW)
**Risk:** Missing VIEW DATABASE STATE breaks execution plan analysis
**Mitigation:**
- Permission check before plan retrieval
- Graceful degradation to static analysis only
- Clear user messaging about missing permissions
- No crashes or errors

---

## 📚 UPDATED DOCUMENTATION REQUIREMENTS

### Files to Update
1. **README.md** - Add new features section + SQL Server 2022 requirements
2. **docs/architecture/QUERY_OPTIMIZER_ARCHITECTURE.md** - Update flow diagrams with Layer 0.5
3. **docs/testing/QUERY_OPTIMIZER_TEST_PLAN.md** - Add semantic validation tests
4. **API Documentation** - Update response schemas with AutoFixResult
5. **User Guide** - Explain new UI components + permission requirements

### New Documentation (Updated)
1. **ANTI_PATTERNS_REFERENCE.md** - Complete list of 25+ patterns with PSP notes
2. **COLUMN_STATISTICS_GUIDE.md** - How to interpret skew/selectivity + cache behavior
3. **EXECUTION_PLAN_ANALYSIS.md** - Understanding plan warnings + permission requirements
4. **OPTIMIZATION_BEST_PRACTICES.md** - DBA-level recommendations + SQL Server 2022 features
5. **SEMANTIC_VALIDATION_GUIDE.md** - How auto-fix validation works
6. **SQL_SERVER_2022_FEATURES.md** - PSP, Query Store, native anti-patterns

---

## ✅ UPDATED DEFINITION OF DONE

### Phase 0 Complete When (NEW):
- [ ] Native anti-pattern detector implemented
- [ ] Query Store integration working
- [ ] Compatibility level detection working
- [ ] PSP awareness in recommendations

### Phase 1 Complete When:
- [ ] 25+ anti-patterns detected
- [ ] AntiPatternContext for false positive suppression
- [ ] All unit tests passing
- [ ] Auto-fix with semantic validation working
- [ ] Keyset pagination recommendation added
- [ ] Code review approved

### Phase 2 Complete When:
- [ ] Column statistics integrated
- [ ] DDL-aware cache invalidation working
- [ ] Statistics freshness check implemented
- [ ] Skew/selectivity in LLM prompt
- [ ] Integration tests passing
- [ ] Performance acceptable (<5s)

### Phase 3 Complete When:
- [ ] Permission check implemented
- [ ] Graceful degradation working
- [ ] Pre-flight analysis working
- [ ] Execution plan warnings parsed
- [ ] Missing indexes extracted
- [ ] Implicit conversions detected
- [ ] Stale statistics detection working

### Phase 4 Complete When:
- [ ] ContextBudgetManager implemented
- [ ] Enhanced prompt deployed
- [ ] Token budget management working
- [ ] LLM responses validated
- [ ] JSON parsing robust
- [ ] Error handling complete

### Phase 5 Complete When:
- [ ] UI components implemented
- [ ] Data skew visualized
- [ ] Cost comparison shown
- [ ] Semantic validation UI added
- [ ] User testing positive

### Phase 6 Complete When:
- [ ] All tests passing (>90% coverage)
- [ ] Semantic validation tests complete
- [ ] Permission handling tests complete
- [ ] E2E tests validated
- [ ] Performance benchmarks met
- [ ] Documentation complete

---

## 🏁 FINAL CONCLUSION

Kế hoạch gốc có nền tảng tốt nhưng thiếu defensive engineering ở các boundary conditions. Sau khi incorporate senior architect review:

### Critical Additions:
✅ **Layer 0.5:** SQL Server 2022 native anti-pattern detection  
✅ **Semantic Validation:** Auto-fix safety với validation queries  
✅ **Permission Handling:** Graceful degradation khi thiếu permissions  
✅ **Token Budget:** Priority-based context truncation  
✅ **PSP Awareness:** SQL Server 2022 compatibility level checks  
✅ **Keyset Pagination:** Best-in-class pagination recommendations  
✅ **False Positive Suppression:** AntiPatternContext cho intelligent detection  
✅ **DDL-Aware Caching:** Statistics invalidation dựa trên last_updated  

### Expected Impact (Updated):
- Detection accuracy: 60% → 95%+ (với false positive suppression)
- Optimization success: 40% → 85%+
- Production safety: Low → High (semantic validation + permission handling)
- SQL Server 2022 awareness: None → Full support
- User satisfaction: Significant improvement

**Timeline:** 3-4 weeks (18-20 working days) - tăng từ 3 weeks do thêm Phase 0  
**Effort:** ~140 hours (tăng từ 120h)  
**Priority:** CRITICAL - Core feature enhancement với production safety

---

**Prepared by:** Kiro AI Assistant  
**Reviewed by:** Senior Architect  
**Date:** 2026-04-10  
**Version:** 2.0 (Post-Review)  
**Status:** Ready for Implementation với Critical Fixes
