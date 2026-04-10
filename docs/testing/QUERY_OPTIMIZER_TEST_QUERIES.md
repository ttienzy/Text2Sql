# Query Optimizer Test Queries

Comprehensive test queries for validating Query Optimizer functionality.

---

## Section 1: Anti-Pattern Tests

### AP-01: SELECT * (Code Quality)
**BAD**:
```sql
SELECT * FROM dbo.Users WHERE Id = 1
```
**Expected Detection**: AP-01, Severity: Warning  
**GOOD**:
```sql
SELECT Id, Name, Email, CreatedDate FROM dbo.Users WHERE Id = 1
```

---

### AP-02: Function on Indexed Column (SARGability - Critical)
**BAD**:
```sql
SELECT * FROM dbo.Users WHERE YEAR(CreatedDate) = 2024
```
**Expected Detection**: AP-02, Severity: Critical  
**GOOD**:
```sql
SELECT * FROM dbo.Users 
WHERE CreatedDate >= '2024-01-01' AND CreatedDate < '2025-01-01'
```

---

### AP-03: Leading Wildcard in LIKE (SARGability - Critical)
**BAD**:
```sql
SELECT * FROM dbo.Users WHERE Name LIKE '%Smith'
```
**Expected Detection**: AP-03, Severity: Critical  
**GOOD**:
```sql
-- Use full-text search or redesign query
SELECT * FROM dbo.Users WHERE Name LIKE 'Smith%'
```

---

### AP-04: NOT IN with Nullable Column (Logic - Serious)
**BAD**:
```sql
SELECT * FROM dbo.Orders WHERE CustomerId NOT IN (SELECT Id FROM dbo.DeletedCustomers)
```
**Expected Detection**: AP-04, Severity: Serious  
**GOOD**:
```sql
SELECT * FROM dbo.Orders o
WHERE NOT EXISTS (SELECT 1 FROM dbo.DeletedCustomers d WHERE d.Id = o.CustomerId)
```

---

### AP-06: OR Chain on Same Column (Performance - Warning)
**BAD**:
```sql
SELECT * FROM dbo.Users WHERE Status='Active' OR Status='Pending' OR Status='New'
```
**Expected Detection**: AP-06, Severity: Warning  
**GOOD**:
```sql
SELECT * FROM dbo.Users WHERE Status IN ('Active', 'Pending', 'New')
```

---

### AP-07: DISTINCT Without Justification (Code Quality - Info)
**BAD** (in OLTP context):
```sql
SELECT DISTINCT Name FROM dbo.Users WHERE Id = 1
```
**Expected Detection**: AP-07, Severity: Info (suppressed in analytical context)  
**GOOD**:
```sql
SELECT Name FROM dbo.Users WHERE Id = 1
```

---

### AP-08: ORDER BY Without LIMIT (Performance - Info)
**BAD** (in OLTP context):
```sql
SELECT * FROM dbo.Orders ORDER BY OrderDate DESC
```
**Expected Detection**: AP-08, Severity: Info (suppressed in analytical context)  
**GOOD**:
```sql
SELECT TOP 100 * FROM dbo.Orders ORDER BY OrderDate DESC
```

---

### AP-09: Scalar Subquery in SELECT (Performance - Warning)
**BAD**:
```sql
SELECT 
    o.OrderId,
    (SELECT COUNT(*) FROM dbo.OrderItems WHERE OrderId = o.OrderId) AS ItemCount
FROM dbo.Orders o
```
**Expected Detection**: AP-09, Severity: Warning  
**GOOD**:
```sql
SELECT 
    o.OrderId,
    COUNT(oi.OrderItemId) AS ItemCount
FROM dbo.Orders o
LEFT JOIN dbo.OrderItems oi ON o.OrderId = oi.OrderId
GROUP BY o.OrderId
```

---

### AP-10: Implicit Type Conversion (SARGability - Critical)
**BAD**:
```sql
SELECT * FROM dbo.Users WHERE Id = '123'  -- Id is int
```
**Expected Detection**: AP-10, Severity: Critical  
**GOOD**:
```sql
SELECT * FROM dbo.Users WHERE Id = 123
```

---

### AP-11: Missing JOIN Predicate (Logic - Critical)
**BAD**:
```sql
SELECT * FROM dbo.Users, dbo.Orders
```
**Expected Detection**: AP-11, Severity: Critical  
**GOOD**:
```sql
SELECT * FROM dbo.Users u
INNER JOIN dbo.Orders o ON u.Id = o.CustomerId
```

---

### AP-12: Correlated Subquery (Performance - Warning)
**BAD**:
```sql
SELECT * FROM dbo.Orders o
WHERE EXISTS (SELECT 1 FROM dbo.OrderItems oi WHERE oi.OrderId = o.OrderId AND oi.Quantity > 10)
```
**Expected Detection**: AP-12, Severity: Warning (if can be optimized to JOIN)  
**GOOD**:
```sql
SELECT DISTINCT o.* FROM dbo.Orders o
INNER JOIN dbo.OrderItems oi ON o.OrderId = oi.OrderId
WHERE oi.Quantity > 10
```

---

### AP-13: Missing Schema Prefix (Code Quality - Info)
**BAD**:
```sql
SELECT * FROM Users WHERE Id = 1
```
**Expected Detection**: AP-13, Severity: Info  
**GOOD**:
```sql
SELECT * FROM dbo.Users WHERE Id = 1
```

---

### AP-14: UNION Instead of UNION ALL (Performance - Warning)
**BAD** (when duplicates don't matter):
```sql
SELECT Name FROM dbo.Users WHERE Status='Active'
UNION
SELECT Name FROM dbo.Users WHERE Status='Pending'
```
**Expected Detection**: AP-14, Severity: Warning  
**GOOD**:
```sql
SELECT Name FROM dbo.Users WHERE Status='Active'
UNION ALL
SELECT Name FROM dbo.Users WHERE Status='Pending'
```

---

### AP-18: OFFSET/FETCH Without Keyset (Performance - Warning)
**BAD** (for large offsets):
```sql
SELECT * FROM dbo.Orders 
ORDER BY OrderDate 
OFFSET 10000 ROWS FETCH NEXT 10 ROWS ONLY
```
**Expected Detection**: AP-18, Severity: Warning  
**GOOD** (keyset pagination):
```sql
SELECT * FROM dbo.Orders 
WHERE OrderDate > @LastOrderDate
ORDER BY OrderDate 
FETCH NEXT 10 ROWS ONLY
```

---

### AP-21: Non-Unicode String Literal (SARGability - Warning)
**BAD** (when column is nvarchar):
```sql
SELECT * FROM dbo.Users WHERE Name = 'John'  -- Name is nvarchar
```
**Expected Detection**: AP-21, Severity: Warning  
**GOOD**:
```sql
SELECT * FROM dbo.Users WHERE Name = N'John'
```

---

### AP-23: COUNT(*) Without WHERE (Performance - Info)
**BAD** (in OLTP context):
```sql
SELECT COUNT(*) FROM dbo.Orders
```
**Expected Detection**: AP-23, Severity: Info (suppressed in analytical context)  
**GOOD**:
```sql
-- Use sys.partitions for table row count
SELECT SUM(rows) FROM sys.partitions 
WHERE object_id = OBJECT_ID('dbo.Orders') AND index_id IN (0,1)
```

---

## Section 2: False Positive Tests

### AP-07 in Analytical Query (Should NOT Flag)
```sql
-- Analytical query - DISTINCT is justified
SELECT DISTINCT 
    YEAR(OrderDate) AS OrderYear,
    MONTH(OrderDate) AS OrderMonth,
    COUNT(*) OVER (PARTITION BY YEAR(OrderDate), MONTH(OrderDate)) AS OrderCount
FROM dbo.Orders
WHERE OrderDate >= '2024-01-01'
```
**Expected**: AP-07 should be suppressed or severity = Info

---

### AP-08 in Reporting Query (Should NOT Flag)
```sql
-- Reporting query - ORDER BY without LIMIT is acceptable
SELECT 
    ProductCategory,
    SUM(TotalAmount) AS TotalSales
FROM dbo.Orders
GROUP BY ProductCategory
ORDER BY TotalSales DESC
```
**Expected**: AP-08 should be suppressed or severity = Info

---

### AP-23 in Aggregate Query (Should Be Info)
```sql
-- Aggregate query - COUNT(*) is part of analysis
SELECT 
    Status,
    COUNT(*) AS StatusCount
FROM dbo.Orders
GROUP BY Status
```
**Expected**: AP-23 severity = Info (not Warning)

---

## Section 3: Data Skew Tests

### High Skew Column (>70%)
```sql
-- Assume Status column has 85% 'Active', 10% 'Inactive', 5% 'Suspended'
SELECT * FROM dbo.Users WHERE Status = 'Active'
```
**Expected**: 
- Column statistics show SkewFactor > 0.7
- Warning about index effectiveness
- PSP recommendation if SQL Server 2022

---

### Low Selectivity Column (<1%)
```sql
-- Assume IsDeleted column has 99.5% FALSE, 0.5% TRUE
SELECT * FROM dbo.Users WHERE IsDeleted = 0
```
**Expected**:
- Selectivity < 0.01
- Recommendation: "Index not recommended - very low selectivity"
- Consider filtered index for minority value (IsDeleted = 1)

---

### Stale Statistics
```sql
-- Query on table with statistics >7 days old or >20% modifications
SELECT * FROM dbo.Orders WHERE CustomerId = 123
```
**Expected**:
- IsStale = true
- StaleWarning: "Statistics last updated YYYY-MM-DD. Consider running UPDATE STATISTICS."

---

## Section 4: Execution Plan Tests

### Full Scan on Large Table
```sql
SELECT * FROM dbo.LargeTable
```
**Expected**:
- Cost driver: "Clustered Index Scan" or "Table Scan"
- High estimated cost
- Recommendation: "Consider adding covering index"

---

### Missing Index
```sql
SELECT * FROM dbo.Orders 
WHERE CustomerId = 123 AND OrderDate > '2024-01-01'
```
**Expected**:
- Missing index recommendation
- Impact > 20%
- CREATE INDEX statement with CustomerId, OrderDate as key columns

---

### Implicit Conversion in Plan
```sql
SELECT * FROM dbo.Users WHERE Id = '123'  -- Id is int
```
**Expected**:
- AP-21 detected by static analyzer
- Execution plan warning: ImplicitConversion
- Recommendation: "Ensure data types match to allow index seek"

---

## Section 5: Permission Tests

### Limited User (No VIEW DATABASE STATE)
```sql
SELECT * FROM dbo.Users WHERE Id = 1
```
**Expected**:
- CanGetExecutionPlan = false
- Warning: "Missing VIEW DATABASE STATE permission"
- Graceful degradation (no crash)
- Static analysis still works

---

### Full Permission User
```sql
SELECT * FROM dbo.Users WHERE Id = 1
```
**Expected**:
- CanGetExecutionPlan = true
- Full execution plan analysis
- Cost drivers, warnings, missing indexes available

---

## Section 6: Complex Multi-Issue Query

### Kitchen Sink Query
```sql
SELECT * FROM Users 
WHERE YEAR(CreatedDate) = 2024 
  AND Status='Active' OR Status='Pending' 
  AND Name = 'John'
```

**Expected Detections**:
1. **AP-01**: SELECT * (Warning)
2. **AP-02**: YEAR() function on indexed column (Critical)
3. **AP-06**: OR chain on Status (Warning)
4. **AP-13**: Missing schema prefix (Info)
5. **AP-21**: Non-unicode literal 'John' if Name is nvarchar (Warning)

**Priority Order**:
1. AP-02 (SARGability - Critical)
2. AP-06 (OR chain - Warning)
3. AP-01 (SELECT * - Warning)
4. AP-21 (Non-unicode - Warning)
5. AP-13 (Schema prefix - Info)

**Optimized SQL**:
```sql
SELECT Id, Name, Email, CreatedDate, Status 
FROM dbo.Users 
WHERE CreatedDate >= '2024-01-01' AND CreatedDate < '2025-01-01'
  AND Status IN ('Active', 'Pending')
  AND Name = N'John'
```

---

## Section 7: Edge Cases

### Empty Result Set
```sql
SELECT * FROM dbo.Users WHERE 1 = 0
```
**Expected**: Should not crash, may detect as already optimal

---

### Very Long Query
```sql
SELECT * FROM dbo.Users WHERE 
  Id = 1 OR Id = 2 OR Id = 3 OR Id = 4 OR Id = 5 OR 
  Id = 6 OR Id = 7 OR Id = 8 OR Id = 9 OR Id = 10 OR
  -- ... (100+ OR conditions)
  Id = 100
```
**Expected**: 
- AP-06 detected
- Recommendation: Use IN clause or temp table

---

### Nested Subqueries
```sql
SELECT * FROM dbo.Orders WHERE CustomerId IN (
  SELECT Id FROM dbo.Users WHERE Status IN (
    SELECT Status FROM dbo.ValidStatuses
  )
)
```
**Expected**: May detect AP-12 (correlated subquery) if applicable

---

## Section 8: PSP (Parameter Sensitivity Plan) Tests

### SQL Server 2022 with PSP Active
```sql
-- Assume compatibility level >= 160, Query Store enabled
SELECT * FROM dbo.Users WHERE Status = @Status
```
**Expected**:
- PSP Active = true
- High skew warning includes: "PSP may handle this automatically"
- Recommendation: "Verify Query Store enabled"

---

### SQL Server 2019 (No PSP)
```sql
-- Assume compatibility level < 160
SELECT * FROM dbo.Users WHERE Status = @Status
```
**Expected**:
- PSP Active = false
- High skew warning includes: "Consider filtered index or OPTION(OPTIMIZE FOR UNKNOWN)"
- Recommendation: "Upgrade to SQL Server 2022 for PSP optimization"

---

## Testing Guidelines

1. **Run tests in order**: Start with Section 1 (anti-patterns), then move to complex scenarios
2. **Verify graceful degradation**: Permission tests should never crash
3. **Check false positives**: Section 2 tests should NOT flag or have Info severity
4. **Validate PSP awareness**: Section 8 tests should show different recommendations based on SQL Server version
5. **Test with real data**: Data skew tests require actual skewed data distribution
6. **Monitor performance**: Complex queries should complete within reasonable time (<10s)

---

## Expected Coverage

- **Anti-Patterns**: 15+ patterns detected
- **False Positives**: <5% false positive rate
- **Graceful Degradation**: 100% (no crashes on permission errors)
- **PSP Awareness**: Correct recommendations for SQL Server 2022 vs older versions
- **Data Skew Detection**: Accurate skew factor calculation (0-1 range)
- **Execution Plan Analysis**: Cost drivers, warnings, missing indexes detected when available
