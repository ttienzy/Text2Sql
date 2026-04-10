# Query Optimizer Phase 1 - Quick Reference Guide

## 🚀 Quick Start

### Using Anti-Pattern Detection

```csharp
// Analyze SQL query
var analyzer = new StaticAnalyzer();
var metadata = await analyzer.AnalyzeAsync(sql, cancellationToken);

// Check detected issues
foreach (var issue in metadata.DetectedIssues)
{
    Console.WriteLine($"[{issue.Code}] {issue.Title}");
    Console.WriteLine($"Severity: {issue.Severity}");
    Console.WriteLine($"Description: {issue.Description}");
    
    if (issue.AutoFixSuggestion != null)
    {
        Console.WriteLine($"Fix: {issue.AutoFixSuggestion}");
    }
}

// Get critical columns for statistics analysis
var visitor = new QueryMetadataVisitor();
var criticalColumns = visitor.GetCriticalColumns();
```

### Using AutoFixer

```csharp
var fixer = new AutoFixer();
var schema = await GetSchemaContext();

// Fix missing schema prefix (High confidence - safe)
var result1 = fixer.FixMissingSchemaPrefix(sql);
if (result1.CanAutoApply)
{
    // Safe to apply automatically
    sql = result1.FixedSql;
}

// Fix SELECT * (Medium confidence - needs validation)
var result2 = fixer.FixSelectStar(sql, schema);
if (result2.RequiresSemanticValidation)
{
    // Execute validation query first
    var isValid = await ExecuteValidationQuery(result2.ValidationQuery);
    if (isValid)
    {
        sql = result2.FixedSql;
    }
}

// Check if all issues can be auto-fixed
if (fixer.CanAutoFix(metadata.DetectedIssues))
{
    // All issues are auto-fixable
}
```

---

## 📋 Anti-Pattern Reference

### Critical Severity (Immediate Action Required)

**AP-01: SELECT ***
```sql
-- ❌ Bad
SELECT * FROM Users

-- ✅ Good
SELECT Id, Name, Email FROM Users
```
- Auto-fix: ✅ Medium confidence
- Category: CodeQuality

**AP-02: Function on Indexed Column**
```sql
-- ❌ Bad
WHERE YEAR(CreatedDate) = 2024

-- ✅ Good
WHERE CreatedDate >= '2024-01-01' AND CreatedDate < '2025-01-01'
```
- Auto-fix: ❌ (LLM handles)
- Category: SARGability

**AP-12: N+1 Query Pattern**
```sql
-- ❌ Bad
SELECT u.Name, (SELECT COUNT(*) FROM Orders WHERE UserId = u.Id) FROM Users u

-- ✅ Good
SELECT u.Name, COUNT(o.Id) FROM Users u LEFT JOIN Orders o ON u.Id = o.UserId GROUP BY u.Name
```
- Auto-fix: ❌ (LLM handles)
- Category: Performance

### Warning Severity (Should Fix)

**AP-04: COUNT(*)**
```sql
-- ⚠️ Warning
SELECT COUNT(*) FROM Users

-- ✅ Better
SELECT COUNT(1) FROM Users
-- or
SELECT COUNT(Id) FROM Users
```
- Auto-fix: ✅ Medium confidence
- Category: CodeQuality

**AP-06: OR Chain**
```sql
-- ⚠️ Warning
WHERE Status='Active' OR Status='Pending' OR Status='Approved'

-- ✅ Better
WHERE Status IN ('Active', 'Pending', 'Approved')
```
- Auto-fix: ✅ Medium confidence
- Category: CodeQuality

**AP-10: Implicit Conversion**
```sql
-- ⚠️ Warning
WHERE DateColumn = '2024-01-01'

-- ✅ Better
WHERE DateColumn = CAST('2024-01-01' AS DATE)
```
- Auto-fix: ❌ (needs schema info)
- Category: SARGability

**AP-11: Missing Table Alias**
```sql
-- ⚠️ Warning
SELECT * FROM Users, Orders WHERE Users.Id = Orders.UserId

-- ✅ Better
SELECT * FROM Users u JOIN Orders o ON u.Id = o.UserId
```
- Auto-fix: ❌ (complex)
- Category: CodeQuality

**AP-13: Missing Schema Prefix**
```sql
-- ⚠️ Warning
SELECT * FROM Users

-- ✅ Better
SELECT * FROM dbo.Users
```
- Auto-fix: ✅ High confidence (safe)
- Category: CodeQuality

**AP-21: varchar/nvarchar Mismatch**
```sql
-- ⚠️ Warning
WHERE Name = 'John'

-- ✅ Better (if Name is nvarchar)
WHERE Name = N'John'
```
- Auto-fix: ✅ Medium confidence
- Category: SARGability

### Info Severity (Consider Fixing)

**AP-07: DISTINCT Usage**
```sql
-- ℹ️ Info (suppressed for analytical queries)
SELECT DISTINCT Name FROM Users

-- ✅ Better (if possible)
SELECT Name FROM Users GROUP BY Name
```
- Auto-fix: ❌
- Category: Performance
- Context-aware: Suppressed for analytical queries

**AP-08: UNION vs UNION ALL**
```sql
-- ℹ️ Info
SELECT Id FROM Users UNION SELECT Id FROM Customers

-- ✅ Better (if duplicates acceptable)
SELECT Id FROM Users UNION ALL SELECT Id FROM Customers
```
- Auto-fix: ❌
- Category: Performance

**AP-14: Missing SET NOCOUNT**
```sql
-- ℹ️ Info
CREATE PROCEDURE GetUsers AS
BEGIN
    SELECT * FROM Users
END

-- ✅ Better
CREATE PROCEDURE GetUsers AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM Users
END
```
- Auto-fix: ✅ High confidence
- Category: Performance

**AP-18: ROW_NUMBER Pagination**
```sql
-- ℹ️ Info
SELECT ROW_NUMBER() OVER (ORDER BY Id) AS RowNum, * FROM Users

-- ✅ Better (small datasets)
SELECT * FROM Users ORDER BY Id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY

-- ✅ Best (large datasets, high page numbers)
SELECT * FROM Users WHERE Id > @LastSeenId ORDER BY Id FETCH NEXT 10 ROWS ONLY
```
- Auto-fix: ✅ Low confidence (context-dependent)
- Category: Performance

**AP-23: Missing WHERE Clause**
```sql
-- ℹ️ Info (suppressed for analytical queries)
SELECT * FROM Users

-- ✅ Better (if filtering needed)
SELECT * FROM Users WHERE IsActive = 1
```
- Auto-fix: ❌
- Category: Performance
- Context-aware: Suppressed for analytical queries

### Error Severity (Logic Error)

**AP-09: HAVING without GROUP BY**
```sql
-- ❌ Error
SELECT Name FROM Users HAVING COUNT(*) > 5

-- ✅ Correct
SELECT Name FROM Users GROUP BY Name HAVING COUNT(*) > 5
```
- Auto-fix: ❌ (logic error)
- Category: Logic

---

## 🎯 Confidence Levels

### High Confidence (Can Auto-Apply)
- **AP-13:** Missing schema prefix
- **AP-14:** Missing SET NOCOUNT
- Safe operations with no semantic risks

### Medium Confidence (Requires Validation)
- **AP-01:** SELECT * expansion
- **AP-04:** COUNT(*) → COUNT(1)
- **AP-06:** OR → IN conversion
- **AP-21:** Add N prefix
- May change semantics, validation query provided

### Low Confidence (Manual Review)
- **AP-18:** Pagination strategy
- Context-dependent, requires domain knowledge

---

## 🔍 Context-Aware Detection

### Analytical Query Detection
Automatically detected when query has:
- GROUP BY clause
- Aggregate functions (COUNT, SUM, AVG, MIN, MAX)
- Window functions

**Suppressed Patterns:**
- AP-07: DISTINCT (common in analytical queries)
- AP-23: Missing WHERE (full table scans acceptable)

### Reporting Query Detection
Automatically detected when query has:
- UNION operations
- Aggregate functions

**Suppressed Patterns:**
- AP-08: UNION (deduplication often intentional)

---

## 🛠️ Helper Methods

### Column Tracking
```csharp
var visitor = new QueryMetadataVisitor();
// ... parse SQL ...

// Get columns by clause
var whereColumns = visitor.GetWhereClauseColumns();
var joinColumns = visitor.GetJoinColumns();
var orderByColumns = visitor.GetOrderByColumns();
var groupByColumns = visitor.GetGroupByColumns();

// Get all critical columns (deduplicated)
var criticalColumns = visitor.GetCriticalColumns();
// Use for Phase 2: ColumnStatisticsService
```

### Semantic Validation
```csharp
var result = fixer.FixSelectStar(sql, schema);

if (result.RequiresSemanticValidation)
{
    // Execute validation query
    var validationSql = result.ValidationQuery;
    
    // Expected result: 'IDENTICAL'
    var validationResult = await ExecuteScalar(validationSql);
    
    if (validationResult == "IDENTICAL")
    {
        // Safe to apply fix
        sql = result.FixedSql;
    }
    else
    {
        // Semantic difference detected
        Console.WriteLine("Fix changes query semantics!");
        foreach (var risk in result.SemanticRisks)
        {
            Console.WriteLine($"Risk: {risk}");
        }
    }
}
```

---

## 📊 Pattern Categories

### SARGability
Patterns affecting index usage:
- AP-02: Function on indexed column
- AP-03: Non-SARGable LIKE
- AP-10: Implicit conversion
- AP-15: ISNULL/COALESCE in WHERE
- AP-21: varchar/nvarchar mismatch

### Performance
Patterns affecting query performance:
- AP-07: DISTINCT abuse
- AP-08: UNION vs UNION ALL
- AP-12: N+1 Query Pattern
- AP-14: Missing SET NOCOUNT
- AP-18: ROW_NUMBER pagination
- AP-23: Missing WHERE clause

### CodeQuality
Patterns affecting code maintainability:
- AP-01: SELECT *
- AP-04: COUNT(*)
- AP-06: OR chain
- AP-11: Missing table alias
- AP-13: Missing schema prefix

### Logic
Patterns indicating logic errors:
- AP-09: HAVING without GROUP BY

### IndexUsage
Patterns related to index utilization:
- AP-05: Missing index usage analysis (Phase 3)
- AP-16: Large IN list

---

## 🧪 Testing Examples

### Test Anti-Pattern Detection
```csharp
[Fact]
public async void AP04_CountStar_ShouldDetect()
{
    var sql = "SELECT COUNT(*) FROM Users";
    var metadata = await _analyzer.AnalyzeAsync(sql, default);
    
    Assert.Contains(metadata.DetectedIssues, i => i.Code == "AP-04");
}
```

### Test Auto-Fix
```csharp
[Fact]
public void FixMissingSchemaPrefix_ShouldBeHighConfidence()
{
    var sql = "SELECT * FROM Users";
    var result = _fixer.FixMissingSchemaPrefix(sql);
    
    Assert.Equal(ConfidenceLevel.High, result.Confidence);
    Assert.True(result.CanAutoApply);
    Assert.Contains("dbo.Users", result.FixedSql);
}
```

### Test Context Awareness
```csharp
[Fact]
public async void AP07_AnalyticalQuery_ShouldNotFlag()
{
    var sql = "SELECT DISTINCT Department, COUNT(*) FROM Users GROUP BY Department";
    var metadata = await _analyzer.AnalyzeAsync(sql, default);
    
    // Should not detect AP-07 for analytical queries
    Assert.DoesNotContain(metadata.DetectedIssues, i => i.Code == "AP-07");
}
```

---

## 🚦 Decision Flow

```
Query Input
    ↓
Static Analysis
    ↓
Issues Detected?
    ├─ No → Return OK
    └─ Yes → Check Auto-Fixable
        ├─ All Auto-Fixable?
        │   ├─ Yes → Check Confidence
        │   │   ├─ High → Auto-Apply
        │   │   └─ Medium → Validate First
        │   └─ No → Send to LLM (Phase 4)
        └─ Mixed → Partial Auto-Fix + LLM
```

---

## 📚 Additional Resources

- Full Implementation: `QUERY_OPTIMIZER_PHASE1_IMPLEMENTATION_COMPLETE.md`
- Comprehensive Plan: `QUERY_OPTIMIZER_COMPREHENSIVE_REFACTOR_PLAN.md`
- Test Queries: `QUERY_OPTIMIZER_TEST_QUERIES.md`

---

**Last Updated:** 2026-04-10  
**Version:** 1.0  
**Status:** Phase 1 Complete
