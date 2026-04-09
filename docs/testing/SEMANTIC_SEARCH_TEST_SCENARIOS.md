# 🧪 Semantic Search - Test Scenarios

**Date:** 2026-04-09  
**Purpose:** Test scenarios để verify Semantic Search hoạt động đúng

---

## 🎯 Test Objectives

1. Verify Vietnamese search works
2. Verify English search works
3. Verify abbreviation search works
4. Verify concept-based search works
5. Verify multi-language support
6. Verify scoring accuracy
7. Verify UI responsiveness

---

## 📋 Test Scenarios

### Scenario 1: Vietnamese Search

**Setup:**
- Database có bảng: `Customers`, `KhachHang`, `Orders`, `Products`
- Semantic tags đã được generate

**Test Cases:**

#### TC1.1: Tìm "khách hàng"
```
Input: "khách hàng"
Expected Results:
  ✅ KhachHang (95-100% match)
  ✅ Customers (90-95% match)
  ✅ CustomerOrders (80-90% match) - if exists
  ❌ Products (should not appear or <50% match)
```

#### TC1.2: Tìm "đơn hàng"
```
Input: "đơn hàng"
Expected Results:
  ✅ Orders (95-100% match)
  ✅ DonHang (95-100% match) - if exists
  ✅ OrderDetails (85-95% match) - if exists
  ❌ Customers (should not appear or <50% match)
```

#### TC1.3: Tìm "sản phẩm"
```
Input: "sản phẩm"
Expected Results:
  ✅ Products (95-100% match)
  ✅ SanPham (95-100% match) - if exists
  ✅ ProductCategories (85-95% match) - if exists
  ✅ Inventory (70-85% match) - related
```

---

### Scenario 2: English Search

**Test Cases:**

#### TC2.1: Tìm "customer"
```
Input: "customer"
Expected Results:
  ✅ Customers (95-100% match)
  ✅ KhachHang (90-95% match)
  ✅ CustomerAccounts (85-95% match) - if exists
  ✅ Users (70-85% match) - related
```

#### TC2.2: Tìm "order"
```
Input: "order"
Expected Results:
  ✅ Orders (95-100% match)
  ✅ DonHang (90-95% match) - if exists
  ✅ SalesOrders (90-95% match) - if exists
  ✅ PurchaseOrders (85-95% match) - if exists
```

#### TC2.3: Tìm "product"
```
Input: "product"
Expected Results:
  ✅ Products (95-100% match)
  ✅ SanPham (90-95% match) - if exists
  ✅ Items (80-90% match) - synonym
  ✅ Goods (75-85% match) - synonym
```

---

### Scenario 3: Abbreviation Search

**Test Cases:**

#### TC3.1: Tìm "KH"
```
Input: "KH"
Expected Results:
  ✅ KhachHang (100% match) - exact abbreviation
  ✅ Customers (90-95% match) - has "KH" tag
  ✅ tblKH (95-100% match) - if exists
  ❌ Orders (should not appear)
```

#### TC3.2: Tìm "DH"
```
Input: "DH"
Expected Results:
  ✅ DonHang (100% match) - exact abbreviation
  ✅ Orders (90-95% match) - has "DH" tag
  ✅ tblDH (95-100% match) - if exists
  ❌ Customers (should not appear)
```

#### TC3.3: Tìm "SP"
```
Input: "SP"
Expected Results:
  ✅ SanPham (100% match) - exact abbreviation
  ✅ Products (90-95% match) - has "SP" tag
  ✅ tblSP (95-100% match) - if exists
  ❌ Orders (should not appear)
```

---

### Scenario 4: Concept-Based Search

**Test Cases:**

#### TC4.1: Tìm "sales" (bán hàng)
```
Input: "sales"
Expected Results:
  ✅ Orders (90-95% match) - sales transaction
  ✅ Invoices (85-95% match) - sales document
  ✅ SalesReps (95-100% match) - sales people
  ✅ Revenue (80-90% match) - sales metric
  ✅ Customers (70-85% match) - sales target
  ✅ Products (65-80% match) - sales item
```

#### TC4.2: Tìm "inventory" (kho)
```
Input: "inventory"
Expected Results:
  ✅ Inventory (95-100% match) - exact
  ✅ Stock (90-95% match) - synonym
  ✅ Warehouses (85-95% match) - related
  ✅ Products (75-85% match) - inventory item
  ✅ Suppliers (65-80% match) - inventory source
```

#### TC4.3: Tìm "user management" (quản lý người dùng)
```
Input: "user management"
Expected Results:
  ✅ Users (95-100% match)
  ✅ Roles (85-95% match)
  ✅ Permissions (85-95% match)
  ✅ UserRoles (90-95% match) - if exists
  ✅ Accounts (80-90% match)
```

---

### Scenario 5: Multi-Language Support

**Test Cases:**

#### TC5.1: Same concept, different languages
```
Input 1: "khách hàng"
Input 2: "customer"
Input 3: "KH"

Expected: All 3 queries should return similar results:
  ✅ Customers
  ✅ KhachHang
  ✅ CustomerAccounts
  
Scores may vary slightly but top 3 should be same tables
```

#### TC5.2: Mixed language query
```
Input: "customer đơn hàng"
Expected Results:
  ✅ CustomerOrders (95-100% match) - if exists
  ✅ Orders (85-95% match)
  ✅ Customers (80-90% match)
```

---

### Scenario 6: Edge Cases

**Test Cases:**

#### TC6.1: Very short query
```
Input: "a"
Expected: Error message "Query must be at least 2 characters"
```

#### TC6.2: Empty query
```
Input: ""
Expected: No search performed, show examples
```

#### TC6.3: Special characters
```
Input: "customer@#$"
Expected: Search for "customer", ignore special chars
```

#### TC6.4: Numbers
```
Input: "123"
Expected: Search for tables with "123" in name or tags
```

#### TC6.5: Very long query
```
Input: "tìm bảng khách hàng có đơn hàng và sản phẩm liên quan đến bán hàng"
Expected: Extract key concepts and search
```

---

### Scenario 7: No Results

**Test Cases:**

#### TC7.1: Irrelevant query
```
Input: "weather forecast"
Expected: 
  - Empty results
  - Message: "No tables found"
  - Suggest trying different keywords
```

#### TC7.2: Typo in query
```
Input: "custmer" (typo)
Expected:
  ✅ Customers (80-90% match) - fuzzy match
  ✅ KhachHang (75-85% match)
```

---

### Scenario 8: Performance

**Test Cases:**

#### TC8.1: Search speed
```
Setup: Database with 500 tables
Input: "customer"
Expected: Results in <500ms
```

#### TC8.2: Multiple searches
```
Perform 10 consecutive searches
Expected: Each search <500ms, no degradation
```

#### TC8.3: Concurrent searches
```
5 users search simultaneously
Expected: All complete in <1s
```

---

## 🎨 UI Test Scenarios

### UI1: Search Input

**Test:**
1. Click search input
2. Type "khách"
3. Verify loading indicator appears
4. Verify results appear after typing stops

**Expected:**
- Input is responsive
- Loading indicator shows
- Results update automatically

### UI2: Example Tags

**Test:**
1. Click example tag "tìm bảng khách hàng"
2. Verify search is triggered
3. Verify results appear

**Expected:**
- Tag click triggers search
- Input field updates
- Results display correctly

### UI3: Result Click

**Test:**
1. Search for "customer"
2. Click on "Customers" result
3. Verify table detail loads

**Expected:**
- Table is selected
- Detail panel shows table info
- Search modal closes (if modal)

### UI4: Score Display

**Test:**
1. Search for "sales"
2. Verify score badges show percentages
3. Verify scores are sorted descending

**Expected:**
- Scores show as "95%", "87%", etc.
- Results sorted by score (highest first)
- Color coding for score ranges

### UI5: Semantic Tags Display

**Test:**
1. Search for "customer"
2. Verify semantic tags show under each result
3. Verify "show more" if >8 tags

**Expected:**
- First 8 tags visible
- "+N more" badge if >8 tags
- Tags are readable and styled

---

## 🔧 Backend Test Scenarios

### Backend1: Qdrant Integration

**Test:**
```bash
# Check if Qdrant collection exists
curl http://localhost:6333/collections/db_explorer_{connectionId}

Expected: Collection exists with points
```

### Backend2: Search Endpoint

**Test:**
```bash
# Test search endpoint
curl -X GET "http://localhost:5251/api/db-explorer/{connectionId}/search?query=customer&limit=10"

Expected:
{
  "query": "customer",
  "resultCount": 5,
  "results": [
    {
      "tableName": "Customers",
      "score": 0.95,
      "semanticTags": ["khách hàng", "customer", "KH"],
      "role": "Master",
      "module": "Sales"
    }
  ]
}
```

### Backend3: Error Handling

**Test:**
```bash
# Test with invalid connectionId
curl -X GET "http://localhost:5251/api/db-explorer/invalid-id/search?query=customer"

Expected: 404 Not Found

# Test with short query
curl -X GET "http://localhost:5251/api/db-explorer/{connectionId}/search?query=a"

Expected: 400 Bad Request "Query must be at least 2 characters"
```

---

## ✅ Acceptance Criteria

### Must Have:
- ✅ Vietnamese search works
- ✅ English search works
- ✅ Abbreviation search works
- ✅ Results sorted by score
- ✅ Semantic tags displayed
- ✅ Click to select table
- ✅ Loading indicator
- ✅ Error handling

### Nice to Have:
- ✅ Example queries
- ✅ Help text
- ✅ Empty state message
- ✅ Score color coding
- ✅ Module/role badges

### Performance:
- ✅ Search <500ms
- ✅ No UI lag
- ✅ Smooth animations

---

## 📊 Test Results Template

```markdown
## Test Execution: [Date]

### Scenario 1: Vietnamese Search
- TC1.1: ✅ PASS - Found KhachHang (98%), Customers (95%)
- TC1.2: ✅ PASS - Found Orders (97%), DonHang (96%)
- TC1.3: ✅ PASS - Found Products (98%), SanPham (97%)

### Scenario 2: English Search
- TC2.1: ✅ PASS - Found Customers (98%), KhachHang (94%)
- TC2.2: ✅ PASS - Found Orders (97%), DonHang (95%)
- TC2.3: ✅ PASS - Found Products (98%), SanPham (94%)

### Scenario 3: Abbreviation Search
- TC3.1: ✅ PASS - Found KhachHang (100%), Customers (94%)
- TC3.2: ✅ PASS - Found DonHang (100%), Orders (95%)
- TC3.3: ✅ PASS - Found SanPham (100%), Products (94%)

### Performance
- Average search time: 250ms
- UI responsiveness: Excellent
- No errors encountered

### Issues Found
- None

### Recommendations
- Consider adding search history
- Add keyboard shortcuts (Ctrl+K)
```

---

**Status:** Ready for testing  
**Priority:** HIGH  
**Estimated Test Time:** 2-3 hours

