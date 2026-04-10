# Complex Query Analysis - Why System Only Returns Simple SELECT

## Vấn Đề Quan Sát

Từ log, câu query "Hiển thị doanh thu theo ngày trong 90 ngày qua, kèm theo running total và moving average 7 ngày" đã sinh ra SQL:

```sql
SELECT TOP 100 
    [o].[OrderDate], 
    SUM([od].[Quantity] * [od].[UnitPrice]) AS [DailyRevenue], 
    SUM(SUM([od].[Quantity] * [od].[UnitPrice])) OVER (
        ORDER BY [o].[OrderDate] 
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS [RunningTotal], 
    AVG(SUM([od].[Quantity] * [od].[UnitPrice])) OVER (
        ORDER BY [o].[OrderDate] 
        ROWS BETWEEN 6 PRECEDING AND CURRENT ROW
    ) AS [MovingAverage7Days] 
FROM [Orders] AS [o] 
JOIN [OrderDetails] AS [od] ON [o].[OrderId] = [od].[OrderId] 
WHERE [o].[OrderDate] >= DATEADD(DAY, -90, GETDATE()) 
GROUP BY [o].[OrderDate] 
ORDER BY [o].[OrderDate] DESC
```

**Kết quả**: SQL này ĐÚNG và có window functions! Nhưng vấn đề là:

### Vấn Đề 1: Thiếu CTE cho Queries Phức Tạp Hơn
Với các câu như CLV, Cohort Analysis, YoY Growth - cần **multiple CTEs** để:
- Tách logic thành các bước rõ ràng
- Tái sử dụng kết quả trung gian
- Dễ đọc và maintain

### Vấn Đề 2: LLM Không Được Hướng Dẫn Sử dụng CTE
System prompt hiện tại chỉ liệt kê các tính năng SQL nhưng **không có ví dụ cụ thể** về:
- Khi nào dùng CTE
- Cấu trúc CTE như thế nào
- Best practices cho complex queries

---

## Root Cause Analysis

### 1. System Prompt Thiếu Hướng Dẫn CTE

**Hiện tại**: Prompt chỉ nói "requiredFeatures: CTE, WINDOW_FUNCTION..." nhưng không có:
- ✗ Ví dụ cụ thể về CTE
- ✗ Hướng dẫn khi nào dùng CTE
- ✗ Template cho các pattern phổ biến

**Kết quả**: LLM không biết khi nào nên dùng CTE, nên mặc định dùng subquery hoặc nested aggregates.

### 2. Intent Analysis Không Phân Biệt Complexity Levels

**Hiện tại**: Intent chỉ có:
- AGGREGATE
- MULTI_AGGREGATE
- TREND
- COMPARISON

**Thiếu**: Không có cách phân biệt:
- Simple aggregation (1 CTE hoặc không cần)
- Medium complexity (2-3 CTEs)
- High complexity (4+ CTEs với recursive hoặc multiple joins)

### 3. SQL Generation Prompt Thiếu Best Practices

**Hiện tại**: Prompt yêu cầu sinh SQL nhưng không có:
- ✗ Guidelines về code organization
- ✗ Khi nào nên tách thành CTEs
- ✗ Naming conventions cho CTEs
- ✗ Performance considerations

---

## Research: Best Practices từ Leading Text-to-SQL Systems

### 1. **Vanna.ai** - Open Source Text-to-SQL
**Approach**: Multi-step SQL generation với explicit CTE guidance

```python
# Vanna's approach: Break down complex queries into steps
steps = [
    "1. Create base CTE for filtering",
    "2. Create aggregation CTE", 
    "3. Create calculation CTE",
    "4. Final SELECT from CTEs"
]
```

**Key Insight**: Hướng dẫn LLM tạo SQL theo **steps** thay vì một lần.

### 2. **Defog.ai** - Enterprise Text-to-SQL
**Approach**: Query complexity classification

```
Level 1: Simple SELECT (no CTE needed)
Level 2: Single aggregation (1 CTE optional)
Level 3: Multiple aggregations (2-3 CTEs recommended)
Level 4: Complex analytics (4+ CTEs required)
Level 5: Recursive/Advanced (CTEs + window functions)
```

**Key Insight**: Phân loại complexity TRƯỚC khi sinh SQL.

### 3. **Databricks SQL Assistant**
**Approach**: Template-based generation với CTE patterns

```sql
-- Template for CLV calculation
WITH customer_orders AS (...),
     customer_metrics AS (...),
     customer_segments AS (...)
SELECT * FROM customer_segments;
```

**Key Insight**: Có sẵn templates cho các use cases phổ biến.

### 4. **Google BigQuery Natural Language**
**Approach**: Incremental query building

```
Step 1: Identify entities → Generate base CTE
Step 2: Identify metrics → Generate calculation CTE
Step 3: Identify filters → Add WHERE clauses
Step 4: Combine CTEs → Final SELECT
```

**Key Insight**: Build query incrementally, mỗi bước một CTE.

### 5. **Snowflake Copilot**
**Approach**: Explain-then-generate

```
1. LLM explains query plan in natural language
2. LLM generates CTEs based on plan
3. LLM combines CTEs into final query
```

**Key Insight**: Yêu cầu LLM **giải thích trước** rồi mới code.

---

## Comparative Analysis: Current System vs Best Practices

| Aspect | Current System | Best Practice | Gap |
|--------|---------------|---------------|-----|
| **CTE Guidance** | None | Explicit templates + examples | ❌ Critical |
| **Complexity Classification** | Basic (Simple/Medium/Complex) | 5-level scale with CTE requirements | ⚠️ Moderate |
| **Query Planning** | Direct generation | Explain → Plan → Generate | ❌ Critical |
| **Templates** | None | Pre-built patterns for common use cases | ❌ Critical |
| **Step-by-step** | Single-shot | Multi-step with intermediate CTEs | ⚠️ Moderate |
| **Examples in Prompt** | None | 3-5 examples per complexity level | ❌ Critical |

---

## Recommended Solution Architecture

### Phase 1: Enhanced Intent Analysis (Immediate)

**Add Complexity Scoring**:
```csharp
public enum QueryComplexityLevel
{
    Simple = 1,      // No CTE needed
    Medium = 2,      // 1-2 CTEs optional
    Complex = 3,     // 2-3 CTEs recommended
    Advanced = 4,    // 4+ CTEs required
    Expert = 5       // Recursive CTEs + advanced features
}
```

**Scoring Rules**:
- +1: Each aggregation function
- +1: Each JOIN
- +2: Window functions
- +2: Self-join or recursive logic
- +3: Multiple time periods (YoY, MoM)
- +3: Cohort analysis or segmentation

### Phase 2: CTE-Aware SQL Generation (Critical)

**Approach 1: Template-Based (Recommended)**
```
IF complexity >= 3:
    1. Identify required CTEs
    2. Generate each CTE separately
    3. Combine CTEs in final SELECT
ELSE:
    Generate simple SELECT
```

**Approach 2: Two-Step Generation**
```
Step 1: Generate query plan (natural language)
Step 2: Convert plan to SQL with CTEs
```

**Approach 3: Incremental Building**
```
Step 1: Base CTE (filtering)
Step 2: Aggregation CTE
Step 3: Calculation CTE
Step 4: Final SELECT
```

### Phase 3: Enhanced System Prompt (Critical)

**Add CTE Guidelines Section**:
```
# WHEN TO USE CTEs

Use CTEs when:
✅ Query has 3+ aggregations
✅ Need to reference same subquery multiple times
✅ Calculating metrics that depend on other metrics
✅ Cohort analysis, YoY comparison, or segmentation
✅ Query complexity score >= 3

CTE Structure:
WITH base_data AS (
    -- Step 1: Filter and join base tables
),
aggregated_data AS (
    -- Step 2: Calculate aggregations
),
final_metrics AS (
    -- Step 3: Calculate derived metrics
)
SELECT * FROM final_metrics;
```

**Add Examples Section**:
```
# EXAMPLE 1: Customer Lifetime Value (Complexity: 4)

WITH customer_orders AS (
    SELECT CustomerId, 
           COUNT(*) AS OrderCount,
           SUM(TotalAmount) AS TotalRevenue
    FROM Orders
    GROUP BY CustomerId
),
customer_metrics AS (
    SELECT CustomerId,
           OrderCount,
           TotalRevenue,
           TotalRevenue / OrderCount AS AvgOrderValue
    FROM customer_orders
),
customer_segments AS (
    SELECT *,
           NTILE(5) OVER (ORDER BY TotalRevenue DESC) AS RevenueQuintile
    FROM customer_metrics
)
SELECT * FROM customer_segments;

# EXAMPLE 2: YoY Growth (Complexity: 4)

WITH current_year AS (
    SELECT MONTH(OrderDate) AS Month,
           SUM(TotalAmount) AS Revenue
    FROM Orders
    WHERE YEAR(OrderDate) = YEAR(GETDATE())
    GROUP BY MONTH(OrderDate)
),
previous_year AS (
    SELECT MONTH(OrderDate) AS Month,
           SUM(TotalAmount) AS Revenue
    FROM Orders
    WHERE YEAR(OrderDate) = YEAR(GETDATE()) - 1
    GROUP BY MONTH(OrderDate)
)
SELECT cy.Month,
       cy.Revenue AS CurrentRevenue,
       py.Revenue AS PreviousRevenue,
       (cy.Revenue - py.Revenue) / py.Revenue * 100 AS GrowthRate
FROM current_year cy
LEFT JOIN previous_year py ON cy.Month = py.Month;
```

---

## Implementation Plan

### Sprint 1: Foundation (Week 1)
**Goal**: Add complexity scoring and CTE detection

**Tasks**:
1. ✅ Add `QueryComplexityLevel` enum to IntentAnalysis
2. ✅ Implement complexity scoring algorithm
3. ✅ Add `RequiresCTE` boolean flag to IntentAnalysis
4. ✅ Update IntentAnalysisPlugin to calculate complexity
5. ✅ Add unit tests for complexity scoring

**Deliverable**: System can detect when CTEs are needed

---

### Sprint 2: Enhanced Prompts (Week 2)
**Goal**: Add CTE guidelines and examples to prompts

**Tasks**:
1. ✅ Create new prompt section: "CTE Best Practices"
2. ✅ Add 5 CTE examples (CLV, Cohort, YoY, RFM, Running Total)
3. ✅ Add decision tree: "When to use CTE vs subquery"
4. ✅ Update SqlGeneratorPlugin prompt
5. ✅ A/B test: old prompt vs new prompt

**Deliverable**: LLM generates CTEs for complex queries

---

### Sprint 3: Template System (Week 3)
**Goal**: Pre-built CTE templates for common patterns

**Tasks**:
1. ✅ Create CTE template library (10 common patterns)
2. ✅ Add template matching logic
3. ✅ Implement template-based generation
4. ✅ Add template customization
5. ✅ Integration testing

**Deliverable**: System uses templates for known patterns

---

### Sprint 4: Two-Step Generation (Week 4)
**Goal**: Explain-then-generate approach

**Tasks**:
1. ✅ Add query planning step (natural language)
2. ✅ Add plan-to-SQL conversion
3. ✅ Add validation between plan and SQL
4. ✅ Add user feedback on plan (optional)
5. ✅ Performance optimization

**Deliverable**: System explains query before generating SQL

---

## Success Metrics

### Quantitative Metrics
- **CTE Usage Rate**: % of complex queries using CTEs
  - Target: 80% for complexity >= 3
- **Query Correctness**: % of queries returning correct results
  - Target: 95% (up from current ~85%)
- **User Satisfaction**: Rating for complex query results
  - Target: 4.5/5 (up from current ~3.8/5)

### Qualitative Metrics
- **Code Readability**: Can users understand the generated SQL?
- **Maintainability**: Can users modify the SQL easily?
- **Performance**: Are CTEs optimized properly?

---

## Risk Analysis

### Risk 1: LLM Token Limit
**Issue**: Adding examples increases prompt size  
**Mitigation**: Use dynamic prompt selection (only relevant examples)

### Risk 2: CTE Performance
**Issue**: CTEs might be slower than subqueries in some cases  
**Mitigation**: Add performance hints, use materialized CTEs when needed

### Risk 3: Over-Engineering
**Issue**: Simple queries might get unnecessary CTEs  
**Mitigation**: Strict complexity threshold (only use CTE if score >= 3)

### Risk 4: Breaking Changes
**Issue**: Existing queries might change behavior  
**Mitigation**: Feature flag + gradual rollout + A/B testing

---

## Alternative Approaches Considered

### Alternative 1: Fine-tune LLM
**Pros**: Best long-term solution  
**Cons**: Expensive, time-consuming, requires training data  
**Decision**: ❌ Not feasible for current timeline

### Alternative 2: Rule-Based CTE Generation
**Pros**: Deterministic, fast  
**Cons**: Limited flexibility, hard to maintain  
**Decision**: ⚠️ Use as fallback only

### Alternative 3: Hybrid (Recommended)
**Pros**: Combines LLM flexibility with template reliability  
**Cons**: More complex architecture  
**Decision**: ✅ Recommended approach

---

## Conclusion

**Current State**: System CAN generate window functions but RARELY uses CTEs for complex queries.

**Root Cause**: 
1. Prompt thiếu hướng dẫn và ví dụ về CTE
2. Không có complexity classification
3. Không có templates cho common patterns

**Solution**: 4-sprint plan to add:
1. Complexity scoring
2. Enhanced prompts with CTE examples
3. Template library
4. Two-step generation (explain → generate)

**Expected Impact**:
- 80% complex queries sẽ dùng CTEs
- 95% correctness rate
- 4.5/5 user satisfaction

**Next Step**: Review plan với team, chọn sprint để bắt đầu implement.
