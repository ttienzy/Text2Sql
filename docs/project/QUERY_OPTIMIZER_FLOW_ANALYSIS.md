# Query Optimizer Flow Analysis - Chi Tiết Kỹ Thuật

## Tổng Quan Vấn Đề

Từ screenshot và code analysis, có 2 vấn đề chính:

1. **API `/api/query-optimizer/analyze` quá đơn giản** - Chỉ phát hiện 1 anti-pattern cơ bản (AP-13: Missing schema prefix)
2. **Compare Execution Plans không hoạt động** - Toggle bật nhưng không có dữ liệu execution plan

## Phân Tích Chi Tiết 2 API Endpoints

### API 1: `/api/query-optimizer/analyze` (Basic Optimization)

#### Flow Xử Lý:
```
1. Check Cache (Redis) → Nếu có → Return cached result
2. Static Analysis (StaticAnalyzer) → Phát hiện anti-patterns bằng AST parsing
3. Schema Enrichment (SchemaEnricher) → Lấy thông tin schema từ database
4. LLM Optimization (OptimizeWithLLMAsync) → Gọi LLM để tối ưu SQL
5. Cache Result → Lưu vào Redis (24 hours TTL)
```

#### Các Layer Xử Lý:

**Layer 1: Static Analysis (~50ms)**
- Sử dụng `TSql160Parser` (Microsoft.SqlServer.TransactSql.ScriptDom)
- Parse SQL thành AST (Abstract Syntax Tree)
- Visitor pattern để detect anti-patterns
- **VẤN ĐỀ**: Chỉ detect được patterns cơ bản qua AST

**Layer 2: Schema Enrichment (~5-10ms)**
- Lấy schema metadata từ database
- Cache trong Redis để tăng tốc
- Cung cấp context cho LLM

**Layer 3: LLM Optimization (~2-5s)**
- Load prompt template từ `Prompts/QueryOptimizer/optimize-query.skprompt.txt`
- Replace placeholders với detected issues và schema context
- Gọi LLM để generate optimized SQL
- Parse JSON response (có xử lý markdown code blocks)

#### Vấn Đề Phát Hiện:

1. **StaticAnalyzer quá đơn giản**
   - Chỉ detect được patterns qua AST structure
   - Không phân tích semantic (ý nghĩa)
   - Không detect được:
     - Non-SARGable predicates (WHERE YEAR(Date) = 2024)
     - Implicit conversions
     - Missing indexes
     - Suboptimal JOIN orders
     - Parameter sniffing issues

2. **QueryMetadataVisitor thiếu rules**
   - Cần xem implementation để biết detect được gì
   - Có thể thiếu nhiều anti-pattern rules

3. **LLM dependency quá cao**
   - Static analysis yếu → LLM phải làm hết
   - Nếu LLM không được train tốt → kết quả kém

### API 2: `/api/query-optimizer/analyze-with-plan` (With Execution Plan)

#### Flow Xử Lý:
```
1. Gọi OptimizeAsync() → Lấy basic optimization result
2. Nếu query KHÔNG thay đổi → Return ngay (PlanComparison = null)
3. Nếu query ĐÃ thay đổi:
   - Gọi ExecutionPlanService.ComparePlansAsync()
   - Get execution plan cho original SQL
   - Get execution plan cho optimized SQL
   - So sánh metrics (cost, operators, warnings)
   - Return kết quả với PlanComparison data
```

#### ExecutionPlanService - Cách Hoạt Động:

**Lấy Execution Plan (Production-Safe):**
```sql
SET SHOWPLAN_XML ON;
-- Query here (KHÔNG execute, chỉ lấy plan)
SET SHOWPLAN_XML OFF;
```

**Parse XML Plan:**
- Extract `StatementSubTreeCost` (total cost)
- Extract operators (Index Scan, Index Seek, Table Scan, etc.)
- Extract warnings (missing indexes, implicit conversions, etc.)
- Extract object names và index names

**So Sánh Plans:**
- `ImprovementFactor = OriginalCost / OptimizedCost`
- `ImprovementPercentage = (1 - OptimizedCost/OriginalCost) * 100`
- Compare operators list
- Compare warnings list

#### Vấn Đề Phát Hiện:

1. **Chỉ chạy khi query ĐÃ thay đổi**
   ```csharp
   if (!basicResult.IsChanged)
   {
       return new OptimizationResultWithPlan { PlanComparison = null };
   }
   ```
   - Nếu LLM không thay đổi query → Không có execution plan comparison
   - Trong screenshot: Query gần như giống nhau → `IsChanged = false` → Không có plan

2. **Không có fallback**
   - Nếu execution plan service fail → Return null
   - Không có error message rõ ràng cho user

3. **Thiếu data skew analysis**
   - Code có `ColumnStatisticsService` nhưng không được sử dụng
   - Không có `DataSkewIndicator` data trong response

## Nguyên Nhân Gốc Rễ

### 1. Static Analysis Quá Yếu

**QueryMetadataVisitor** cần được kiểm tra:
- Có bao nhiêu anti-pattern rules?
- Detect được những gì?
- Thiếu những rules nào?

**Cần thêm:**
- Non-SARGable predicate detection
- Implicit conversion detection
- SELECT * detection (đã có AP-13 nhưng chỉ check schema prefix)
- Missing WHERE clause detection
- Cartesian product detection
- Function on indexed column detection

### 2. LLM Prompt Chưa Tối Ưu

Prompt template có thể:
- Không đủ chi tiết về anti-patterns
- Không có examples tốt
- Không guide LLM đủ rõ ràng

### 3. Schema Context Thiếu Thông Tin

`SchemaEnricher` có thể thiếu:
- Index information
- Statistics information
- Foreign key relationships
- Data distribution hints

### 4. Execution Plan Comparison Logic Sai

**Bug Logic:**
```csharp
if (!basicResult.IsChanged)  // ← Sai logic!
{
    return new OptimizationResultWithPlan { PlanComparison = null };
}
```

**Vấn đề:**
- Ngay cả khi query không thay đổi, vẫn nên show execution plan
- User muốn xem plan để hiểu query hiện tại
- Nên compare với chính nó để show metrics

**Fix:**
```csharp
// Luôn lấy execution plan, bất kể query có thay đổi hay không
var originalPlan = await _executionPlanService.GetEstimatedPlanAsync(
    basicResult.OriginalSql, connectionString, cancellationToken);

if (!basicResult.IsChanged)
{
    // Nếu không thay đổi, show plan của original query
    return new OptimizationResultWithPlan
    {
        ...
        PlanComparison = new PlanComparison
        {
            OriginalCost = originalPlan.EstimatedTotalCost,
            OptimizedCost = originalPlan.EstimatedTotalCost,
            ImprovementFactor = 1.0,
            ImprovementPercentage = 0,
            IsImproved = false,
            ImprovementDescription = "Query is already optimal",
            OriginalOperators = originalPlan.Operators,
            OptimizedOperators = originalPlan.Operators,
            OriginalWarnings = originalPlan.Warnings,
            OptimizedWarnings = originalPlan.Warnings
        }
    };
}
```

### 5. Column Statistics Service Không Được Sử Dụng

`ColumnStatisticsService` đã được implement nhưng:
- Không được gọi trong flow
- Frontend có `DataSkewIndicator` component nhưng không có data
- Cần integrate vào flow

## Kế Hoạch Sửa Lỗi

### Priority 1: Fix Execution Plan Logic (Immediate)

1. **Sửa `OptimizeWithPlanComparisonAsync`**
   - Luôn lấy execution plan, không check `IsChanged`
   - Show plan ngay cả khi query không thay đổi
   - Add better error handling

2. **Test với query trong screenshot**
   - Verify execution plan được return
   - Verify UI hiển thị đúng

### Priority 2: Enhance Static Analysis (Short-term)

1. **Review và enhance `QueryMetadataVisitor`**
   - Add thêm anti-pattern detection rules
   - Implement semantic analysis
   - Add more detailed location information

2. **Add new analyzers:**
   - `SARGabilityAnalyzer` - Detect non-SARGable predicates
   - `ImplicitConversionAnalyzer` - Detect type mismatches
   - `IndexUsageAnalyzer` - Suggest missing indexes

### Priority 3: Integrate Column Statistics (Medium-term)

1. **Call `ColumnStatisticsService` trong flow**
2. **Return data skew information**
3. **Update frontend để hiển thị**

### Priority 4: Improve LLM Prompt (Medium-term)

1. **Review và enhance prompt template**
2. **Add more examples**
3. **Add structured output format**

## Testing Plan

### Test Case 1: Query Không Thay Đổi
```sql
SELECT TOP 100 * FROM Customers ORDER BY FullName
```
**Expected:**
- `IsChanged = false`
- `PlanComparison` có data (không null)
- Show execution plan với operators
- Show warnings nếu có

### Test Case 2: Query Có Anti-Patterns
```sql
SELECT * FROM Orders WHERE YEAR(OrderDate) = 2024
```
**Expected:**
- Detect non-SARGable predicate
- Suggest rewrite: `WHERE OrderDate >= '2024-01-01' AND OrderDate < '2025-01-01'`
- Show execution plan comparison
- Show improvement metrics

### Test Case 3: Query Phức Tạp
```sql
SELECT o.*, c.*, p.*
FROM Orders o
JOIN Customers c ON o.CustomerId = c.Id
JOIN Products p ON o.ProductId = p.Id
WHERE c.City = 'Hanoi'
```
**Expected:**
- Detect SELECT *
- Suggest specific columns
- Analyze JOIN order
- Show execution plan với JOIN operators
- Suggest indexes nếu thiếu

## Kết Luận

**Vấn đề chính:**
1. ❌ Static analysis quá yếu - chỉ detect được patterns cơ bản
2. ❌ Execution plan logic sai - không show khi query không thay đổi
3. ❌ Column statistics không được sử dụng
4. ❌ LLM prompt có thể chưa tối ưu

**Giải pháp:**
1. ✅ Fix execution plan logic ngay lập tức
2. ✅ Enhance static analysis với more rules
3. ✅ Integrate column statistics
4. ✅ Improve LLM prompt template

**Next Steps:**
1. Fix `OptimizeWithPlanComparisonAsync` để luôn return execution plan
2. Review `QueryMetadataVisitor` implementation
3. Add more anti-pattern detection rules
4. Test với real queries
