# Natural Language Test Cases

**Purpose**: Test cases để verify hệ thống xử lý câu hỏi tự nhiên đúng  
**Date**: 2026-04-08  
**Database**: Giả định có schema: Customers, Orders, Products, OrderDetails, Employees

---

## 📋 Test Case Structure

Mỗi case gồm:
- **Simple**: Câu hỏi đơn giản, trực tiếp
- **Medium**: Câu hỏi phức tạp hơn, có điều kiện hoặc aggregation

---

## 🔍 Case 1: Basic SELECT Queries

### Test 1.1 - Simple: List All Records
**Question (EN)**: "Show all customers"  
**Question (VI)**: "Liệt kê tất cả khách hàng"

**Expected Intent**: QUERY  
**Expected SQL Pattern**: 
```sql
SELECT * FROM Customers
```

**Expected Result**: 
- Success: true
- Columns: CustomerID, CustomerName, Email, Phone, Address
- RowCount: > 0

---

### Test 1.2 - Medium: List with Condition
**Question (EN)**: "Show customers from Vietnam"  
**Question (VI)**: "Hiển thị khách hàng ở Việt Nam"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT * FROM Customers 
WHERE Country = 'Vietnam' OR Country = 'Việt Nam'
```

**Expected Result**:
- Success: true
- Filtered by country
- RowCount: >= 0

---

## 📊 Case 2: Aggregation Queries

### Test 2.1 - Simple: Count Records
**Question (EN)**: "How many customers do we have?"  
**Question (VI)**: "Có bao nhiêu khách hàng?"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT COUNT(*) as TotalCustomers FROM Customers
```

**Expected Result**:
- Success: true
- Single row with count
- Column: TotalCustomers

---

### Test 2.2 - Medium: Count with Grouping
**Question (EN)**: "How many orders per customer?"  
**Question (VI)**: "Mỗi khách hàng có bao nhiêu đơn hàng?"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT 
    c.CustomerName,
    COUNT(o.OrderID) as OrderCount
FROM Customers c
LEFT JOIN Orders o ON c.CustomerID = o.CustomerID
GROUP BY c.CustomerID, c.CustomerName
ORDER BY OrderCount DESC
```

**Expected Result**:
- Success: true
- Columns: CustomerName, OrderCount
- Grouped by customer

---

## 💰 Case 3: Financial Calculations

### Test 3.1 - Simple: Sum Total
**Question (EN)**: "What is the total revenue?"  
**Question (VI)**: "Tổng doanh thu là bao nhiêu?"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT SUM(TotalAmount) as TotalRevenue 
FROM Orders
```

**Expected Result**:
- Success: true
- Single row with sum
- Column: TotalRevenue

---

### Test 3.2 - Medium: Sum with Date Filter
**Question (EN)**: "What is the total revenue in 2024?"  
**Question (VI)**: "Doanh thu năm 2024 là bao nhiêu?"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT SUM(TotalAmount) as TotalRevenue 
FROM Orders
WHERE YEAR(OrderDate) = 2024
```

**Expected Result**:
- Success: true
- Filtered by year
- Single value

---

## 🔗 Case 4: JOIN Queries

### Test 4.1 - Simple: Basic JOIN
**Question (EN)**: "Show orders with customer names"  
**Question (VI)**: "Hiển thị đơn hàng kèm tên khách hàng"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT 
    o.OrderID,
    o.OrderDate,
    c.CustomerName,
    o.TotalAmount
FROM Orders o
JOIN Customers c ON o.CustomerID = c.CustomerID
```

**Expected Result**:
- Success: true
- Columns include both Orders and Customers fields
- Proper JOIN

---

### Test 4.2 - Medium: Multiple JOINs
**Question (EN)**: "Show order details with product names and customer names"  
**Question (VI)**: "Hiển thị chi tiết đơn hàng kèm tên sản phẩm và tên khách hàng"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT 
    o.OrderID,
    c.CustomerName,
    p.ProductName,
    od.Quantity,
    od.UnitPrice
FROM Orders o
JOIN Customers c ON o.CustomerID = c.CustomerID
JOIN OrderDetails od ON o.OrderID = od.OrderID
JOIN Products p ON od.ProductID = p.ProductID
```

**Expected Result**:
- Success: true
- Multiple tables joined
- Denormalized view

---

## 📈 Case 5: Sorting and Ranking

### Test 5.1 - Simple: Sort by Column
**Question (EN)**: "Show customers sorted by name"  
**Question (VI)**: "Hiển thị khách hàng sắp xếp theo tên"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT * FROM Customers
ORDER BY CustomerName ASC
```

**Expected Result**:
- Success: true
- Sorted alphabetically
- All customers

---

### Test 5.2 - Medium: Top N with Condition
**Question (EN)**: "Show top 10 customers with highest total orders"  
**Question (VI)**: "Hiển thị 10 khách hàng có tổng đơn hàng cao nhất"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT TOP 10
    c.CustomerName,
    SUM(o.TotalAmount) as TotalSpent
FROM Customers c
JOIN Orders o ON c.CustomerID = o.CustomerID
GROUP BY c.CustomerID, c.CustomerName
ORDER BY TotalSpent DESC
```

**Expected Result**:
- Success: true
- Limited to 10 rows
- Sorted by total descending

---

## 🔍 Case 6: Search and Filter

### Test 6.1 - Simple: Exact Match
**Question (EN)**: "Find customer with ID 123"  
**Question (VI)**: "Tìm khách hàng có ID 123"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT * FROM Customers
WHERE CustomerID = 123
```

**Expected Result**:
- Success: true
- Single row or empty
- Exact match

---

### Test 6.2 - Medium: Pattern Match
**Question (EN)**: "Find customers whose name contains 'Nguyen'"  
**Question (VI)**: "Tìm khách hàng có tên chứa 'Nguyễn'"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT * FROM Customers
WHERE CustomerName LIKE '%Nguyen%' OR CustomerName LIKE '%Nguyễn%'
```

**Expected Result**:
- Success: true
- Multiple rows possible
- Pattern matching

---

## 📅 Case 7: Date Range Queries

### Test 7.1 - Simple: Single Date Filter
**Question (EN)**: "Show orders from today"  
**Question (VI)**: "Hiển thị đơn hàng hôm nay"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT * FROM Orders
WHERE CAST(OrderDate AS DATE) = CAST(GETDATE() AS DATE)
```

**Expected Result**:
- Success: true
- Today's orders only
- Date comparison

---

### Test 7.2 - Medium: Date Range
**Question (EN)**: "Show orders between January 1 and March 31, 2024"  
**Question (VI)**: "Hiển thị đơn hàng từ ngày 1/1/2024 đến 31/3/2024"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT * FROM Orders
WHERE OrderDate >= '2024-01-01' AND OrderDate <= '2024-03-31'
```

**Expected Result**:
- Success: true
- Q1 2024 orders
- Range filter

---

## ➕ Case 8: INSERT Operations

### Test 8.1 - Simple: Insert Single Record
**Question (EN)**: "Add a new customer named John Doe with email john@example.com"  
**Question (VI)**: "Thêm khách hàng mới tên John Doe với email john@example.com"

**Expected Intent**: INSERT  
**Expected Route**: WRITE  
**Expected SQL Pattern**:
```sql
INSERT INTO Customers (CustomerName, Email)
VALUES ('John Doe', 'john@example.com')
```

**Expected Behavior**:
- Shows preview first
- Requires confirmation
- Returns affected rows = 1

---

### Test 8.2 - Medium: Insert with Multiple Fields
**Question (EN)**: "Register new customer: name 'Jane Smith', email 'jane@example.com', phone '0123456789', address 'Hanoi'"  
**Question (VI)**: "Đăng ký khách hàng mới: tên 'Jane Smith', email 'jane@example.com', số điện thoại '0123456789', địa chỉ 'Hà Nội'"

**Expected Intent**: INSERT  
**Expected Route**: WRITE  
**Expected SQL Pattern**:
```sql
INSERT INTO Customers (CustomerName, Email, Phone, Address)
VALUES ('Jane Smith', 'jane@example.com', '0123456789', 'Hanoi')
```

**Expected Behavior**:
- Shows preview with all fields
- Requires confirmation
- Returns affected rows = 1

---

## ✏️ Case 9: UPDATE Operations

### Test 9.1 - Simple: Update Single Field
**Question (EN)**: "Change customer email to newemail@example.com where ID is 123"  
**Question (VI)**: "Đổi email khách hàng thành newemail@example.com với ID 123"

**Expected Intent**: UPDATE  
**Expected Route**: WRITE  
**Expected SQL Pattern**:
```sql
UPDATE Customers
SET Email = 'newemail@example.com'
WHERE CustomerID = 123
```

**Expected Behavior**:
- Shows preview with affected rows
- Requires confirmation
- Returns affected rows count

---

### Test 9.2 - Medium: Update Multiple Fields with Condition
**Question (EN)**: "Update customer 123: change phone to '0987654321' and address to 'Ho Chi Minh City'"  
**Question (VI)**: "Cập nhật khách hàng 123: đổi số điện thoại thành '0987654321' và địa chỉ thành 'TP.HCM'"

**Expected Intent**: UPDATE  
**Expected Route**: WRITE  
**Expected SQL Pattern**:
```sql
UPDATE Customers
SET Phone = '0987654321', Address = 'Ho Chi Minh City'
WHERE CustomerID = 123
```

**Expected Behavior**:
- Shows preview with multiple field changes
- Requires confirmation
- Returns affected rows = 1

---

## 🗑️ Case 10: FORBIDDEN Operations (Should Block)

### Test 10.1 - Simple: DELETE Single Record
**Question (EN)**: "Delete customer with ID 123"  
**Question (VI)**: "Xóa khách hàng có ID 123"

**Expected Intent**: FORBIDDEN  
**Expected Route**: FORBIDDEN  
**Expected Behavior**:
- ❌ Operation BLOCKED
- Shows warning message
- Suggests safe alternatives (soft delete, archive)
- NO SQL executed

---

### Test 10.2 - Medium: DROP TABLE
**Question (EN)**: "Drop the Customers table"  
**Question (VI)**: "Xóa bảng Customers"

**Expected Intent**: FORBIDDEN  
**Expected Route**: FORBIDDEN  
**Expected Behavior**:
- ❌ Operation BLOCKED
- Shows critical warning
- Explains why it's dangerous
- NO SQL executed

---

## 🔄 Case 11: Conversation Context (Follow-up Questions)

### Test 11.1 - Simple: Pronoun Resolution
**First Question**: "Show customer with ID 123"  
**Follow-up**: "Show their orders"

**Expected Behavior**:
- First query: SELECT * FROM Customers WHERE CustomerID = 123
- Follow-up resolves "their" → customer 123
- Second query: SELECT * FROM Orders WHERE CustomerID = 123

---

### Test 11.2 - Medium: Context Continuation
**First Question**: "Show orders from January 2024"  
**Follow-up**: "What is the total amount?"

**Expected Behavior**:
- First query: SELECT * FROM Orders WHERE YEAR(OrderDate) = 2024 AND MONTH(OrderDate) = 1
- Follow-up understands context (January 2024 orders)
- Second query: SELECT SUM(TotalAmount) FROM Orders WHERE YEAR(OrderDate) = 2024 AND MONTH(OrderDate) = 1

---

## 🎯 Case 12: Complex Business Questions

### Test 12.1 - Simple: Average Calculation
**Question (EN)**: "What is the average order value?"  
**Question (VI)**: "Giá trị đơn hàng trung bình là bao nhiêu?"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT AVG(TotalAmount) as AverageOrderValue
FROM Orders
```

**Expected Result**:
- Success: true
- Single value
- Proper aggregation

---

### Test 12.2 - Medium: Complex Aggregation with Multiple Conditions
**Question (EN)**: "Show monthly revenue for 2024, sorted by month"  
**Question (VI)**: "Hiển thị doanh thu theo tháng năm 2024, sắp xếp theo tháng"

**Expected Intent**: QUERY  
**Expected SQL Pattern**:
```sql
SELECT 
    MONTH(OrderDate) as Month,
    SUM(TotalAmount) as MonthlyRevenue
FROM Orders
WHERE YEAR(OrderDate) = 2024
GROUP BY MONTH(OrderDate)
ORDER BY Month ASC
```

**Expected Result**:
- Success: true
- 12 rows (one per month)
- Grouped and sorted

---

## 🧪 Testing Guidelines

### How to Run Tests

1. **Manual Testing**:
   - Copy question vào chat interface
   - Verify intent classification
   - Check generated SQL
   - Validate results

2. **Automated Testing**:
   ```bash
   # Run test suite
   dotnet test --filter "Category=NaturalLanguage"
   ```

3. **Expected Success Criteria**:
   - ✅ Intent classified correctly
   - ✅ SQL generated is valid
   - ✅ SQL matches expected pattern
   - ✅ Results returned successfully
   - ✅ No errors or exceptions

### Test Metrics

**Target Success Rate**: 
- Simple queries: 95%+
- Medium queries: 85%+
- Complex queries: 75%+

**Performance Targets**:
- Intent classification: < 500ms
- SQL generation: < 2s
- Total response time: < 5s

---

## 📝 Notes

1. **Database Schema**: Tests assume standard e-commerce schema
2. **Language Support**: Both English and Vietnamese
3. **Intent Routing**: Tests verify correct pipeline routing (QUERY/WRITE/DDL/FORBIDDEN)
4. **Confirmation Flow**: WRITE operations require user confirmation
5. **Security**: FORBIDDEN operations should be blocked completely

---

## 🔄 Test Execution Checklist

- [ ] All QUERY tests pass
- [ ] All INSERT tests show preview + require confirmation
- [ ] All UPDATE tests show preview + require confirmation
- [ ] All FORBIDDEN tests are blocked
- [ ] Conversation context works (pronouns resolved)
- [ ] Both English and Vietnamese work
- [ ] Performance targets met
- [ ] No SQL injection vulnerabilities
- [ ] Error handling works properly
- [ ] Logging captures all operations

---

**Last Updated**: 2026-04-08  
**Status**: Ready for testing  
**Next Review**: After Sprint 3 completion
