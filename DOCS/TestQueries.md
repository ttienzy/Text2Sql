# SQL Test Queries - TextToSqlTest Database

This document contains SQL test queries ranging from basic to advanced levels. Each query is provided in both English and Vietnamese to test the Text-to-SQL agent's capabilities.

## Database Schema Overview

The database `TextToSqlTest` contains 12 main tables:

- **Customers** - Customer information
- **Categories** - Product categories (hierarchical)
- **Products** - Product inventory
- **Suppliers** - Supplier information
- **ProductSuppliers** - Many-to-many relationship between Products and Suppliers
- **Employees** - Employee records
- **Orders** - Customer orders
- **OrderDetails** - Order line items
- **Promotions** - Marketing promotions
- **OrderPromotions** - Orders linked to promotions
- **InventoryTransactions** - Stock movement history
- **ProductReviews** - Customer product reviews

---

## Level 1: Basic Queries (Cơ bản)

### 1.1 Simple SELECT - All Columns

**English:** Show all customers

```sql
SELECT * FROM Customers;
```

**Vietnamese:** Hiển thị tất cả khách hàng

```sql
SELECT * FROM Customers;
```

---

### 1.2 SELECT Specific Columns

**English:** Get customer code, full name, email and phone from customers

```sql
SELECT CustomerCode, FullName, Email, Phone
FROM Customers;
```

**Vietnamese:** Lấy mã khách hàng, họ tên, email và số điện thoại từ bảng khách hàng

```sql
SELECT CustomerCode, FullName, Email, Phone
FROM Customers;
```

---

### 1.3 WHERE Clause - Simple Filter

**English:** Find all VIP customers

```sql
SELECT CustomerCode, FullName, City, CustomerType
FROM Customers
WHERE CustomerType = 'VIP';
```

**Vietnamese:** Tìm tất cả khách hàng VIP

```sql
SELECT CustomerCode, FullName, City, CustomerType
FROM Customers
WHERE CustomerType = 'VIP';
```

---

### 1.4 WHERE with Multiple Conditions

**English:** Get products with price greater than 10 million and stock quantity above 20

```sql
SELECT ProductCode, ProductName, Brand, UnitPrice, StockQuantity
FROM Products
WHERE UnitPrice > 10000000 AND StockQuantity > 20;
```

**Vietnamese:** Lấy sản phẩm có giá trên 10 triệu và số lượng tồn kho trên 20

```sql
SELECT ProductCode, ProductName, Brand, UnitPrice, StockQuantity
FROM Products
WHERE UnitPrice > 10000000 AND StockQuantity > 20;
```

---

### 1.5 ORDER BY

**English:** List all products sorted by price from highest to lowest

```sql
SELECT ProductName, Brand, UnitPrice, StockQuantity
FROM Products
ORDER BY UnitPrice DESC;
```

**Vietnamese:** Liệt kê sản phẩm sp theo giắp xếá từ cao xuống thấp

```sql
SELECT ProductName, Brand, UnitPrice, StockQuantity
FROM Products
ORDER BY UnitPrice DESC;
```

---

### 1.6 LIMIT / TOP

**English:** Show top 5 most expensive products

```sql
SELECT TOP 5 ProductCode, ProductName, Brand, UnitPrice
FROM Products
ORDER BY UnitPrice DESC;
```

**Vietnamese:** Hiển thị 5 sản phẩm có giá cao nhất

```sql
SELECT TOP 5 ProductCode, ProductName, Brand, UnitPrice
FROM Products
ORDER BY UnitPrice DESC;
```

---

### 1.7 LIKE Pattern Matching

**English:** Find customers whose name contains "An"

```sql
SELECT CustomerCode, FullName, Email, Phone
FROM Customers
WHERE FullName LIKE '%An%';
```

**Vietnamese:** Tìm khách hàng có tên chứa "An"

```sql
SELECT CustomerCode, FullName, Email, Phone
FROM Customers
WHERE FullName LIKE N'%An%';
```

---

### 1.8 IN Operator

**English:** Get products from Apple or Samsung brand

```sql
SELECT ProductCode, ProductName, Brand, UnitPrice
FROM Products
WHERE Brand IN ('Apple', 'Samsung');
```

**Vietnamese:** Lấy sản phẩm của hãng Apple hoặc Samsung

```sql
SELECT ProductCode, ProductName, Brand, UnitPrice
FROM Products
WHERE Brand IN (N'Apple', N'Samsung');
```

---

### 1.9 BETWEEN Operator

**English:** Find orders with total amount between 10 million and 30 million

```sql
SELECT OrderCode, CustomerId, OrderDate, TotalAmount, Status
FROM Orders
WHERE TotalAmount BETWEEN 10000000 AND 30000000;
```

**Vietnamese:** Tìm đơn hàng có tổng tiền từ 10 triệu đến 30 triệu

```sql
SELECT OrderCode, CustomerId, OrderDate, TotalAmount, Status
FROM Orders
WHERE TotalAmount BETWEEN 10000000 AND 30000000;
```

---

### 1.10 NULL Values

**English:** Find customers who have not made any purchase yet (no last purchase date)

```sql
SELECT CustomerCode, FullName, Email, RegistrationDate, LastPurchaseDate
FROM Customers
WHERE LastPurchaseDate IS NULL;
```

**Vietnamese:** Tìm khách hàng chưa mua hàng lần nào

```sql
SELECT CustomerCode, FullName, Email, RegistrationDate, LastPurchaseDate
FROM Customers
WHERE LastPurchaseDate IS NULL;
```

---

## Level 2: Intermediate Queries (Trung cấp)

### 2.1 INNER JOIN - Two Tables

**English:** Get order details with product names and quantities

```sql
SELECT
    od.OrderDetailId,
    od.OrderId,
    p.ProductName,
    od.Quantity,
    od.UnitPrice,
    od.LineTotal
FROM OrderDetails od
INNER JOIN Products p ON od.ProductId = p.ProductId;
```

**Vietnamese:** Lấy chi tiết đơn hàng kèm tên sản phẩm và số lượng

```sql
SELECT
    od.OrderDetailId,
    od.OrderId,
    p.ProductName,
    od.Quantity,
    od.UnitPrice,
    od.LineTotal
FROM OrderDetails od
INNER JOIN Products p ON od.ProductId = p.ProductId;
```

---

### 2.2 LEFT JOIN

**English:** Show all customers and their orders (including customers without orders)

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    o.OrderCode,
    o.OrderDate,
    o.TotalAmount
FROM Customers c
LEFT JOIN Orders o ON c.CustomerId = o.CustomerId;
```

**Vietnamese:** Hiển thị tất cả khách hàng và đơn hàng của họ

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    o.OrderCode,
    o.OrderDate,
    o.TotalAmount
FROM Customers c
LEFT JOIN Orders o ON c.CustomerId = o.CustomerId;
```

---

### 2.3 Multiple JOINs

**English:** Get orders with customer name and employee name who processed the order

```sql
SELECT
    o.OrderCode,
    c.FullName AS CustomerName,
    e.FullName AS EmployeeName,
    o.OrderDate,
    o.TotalAmount,
    o.Status
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.CustomerId
INNER JOIN Employees e ON o.EmployeeId = e.EmployeeId;
```

**Vietnamese:** Lấy đơn hàng kèm tên khách hàng và tên nhân viên xử lý

```sql
SELECT
    o.OrderCode,
    c.FullName AS CustomerName,
    e.FullName AS EmployeeName,
    o.OrderDate,
    o.TotalAmount,
    o.Status
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.CustomerId
INNER JOIN Employees e ON o.EmployeeId = e.EmployeeId;
```

---

### 2.4 GROUP BY with Aggregate Functions

**English:** Count orders by status

```sql
SELECT
    Status,
    COUNT(*) AS OrderCount
FROM Orders
GROUP BY Status;
```

**Vietnamese:** Đếm số đơn hàng theo trạng thái

```sql
SELECT
    Status,
    COUNT(*) AS OrderCount
FROM Orders
GROUP BY Status;
```

---

### 2.5 GROUP BY with Multiple Aggregates

**English:** Calculate total revenue and average order value by customer

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    COUNT(o.OrderId) AS TotalOrders,
    SUM(o.TotalAmount) AS TotalSpent,
    AVG(o.TotalAmount) AS AverageOrderValue
FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId
GROUP BY c.CustomerCode, c.FullName;
```

**Vietnamese:** Tính tổng doanh thu và giá trị đơn hàng trung bình theo khách hàng

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    COUNT(o.OrderId) AS TotalOrders,
    SUM(o.TotalAmount) AS TotalSpent,
    AVG(o.TotalAmount) AS AverageOrderValue
FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId
GROUP BY c.CustomerCode, c.FullName;
```

---

### 2.6 HAVING Clause

**English:** Find customers who have placed more than 3 orders

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    COUNT(o.OrderId) AS OrderCount
FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId
GROUP BY c.CustomerCode, c.FullName
HAVING COUNT(o.OrderId) > 3;
```

**Vietnamese:** Tìm khách hàng đã đặt hàng nhiều hơn 3 lần

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    COUNT(o.OrderId) AS OrderCount
FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId
GROUP BY c.CustomerCode, c.FullName
HAVING COUNT(o.OrderId) > 3;
```

---

### 2.7 GROUP BY with ORDER BY

**English:** Show total sales by month in 2024

```sql
SELECT
    YEAR(OrderDate) AS Year,
    MONTH(OrderDate) AS Month,
    COUNT(OrderId) AS OrderCount,
    SUM(TotalAmount) AS MonthlyRevenue
FROM Orders
WHERE YEAR(OrderDate) = 2024
GROUP BY YEAR(OrderDate), MONTH(OrderDate)
ORDER BY Year, Month;
```

**Vietnamese:** Hiển thị tổng doanh thu theo tháng trong năm 2024

```sql
SELECT
    YEAR(OrderDate) AS Year,
    MONTH(OrderDate) AS Month,
    COUNT(OrderId) AS OrderCount,
    SUM(TotalAmount) AS MonthlyRevenue
FROM Orders
WHERE YEAR(OrderDate) = 2024
GROUP BY YEAR(OrderDate), MONTH(OrderDate)
ORDER BY Year, Month;
```

---

### 2.8 Subquery in WHERE

**English:** Find products that have never been ordered

```sql
SELECT
    ProductCode,
    ProductName,
    Brand,
    UnitPrice,
    StockQuantity
FROM Products
WHERE ProductId NOT IN (
    SELECT DISTINCT ProductId
    FROM OrderDetails
);
```

**Vietnamese:** Tìm sản phẩm chưa được đặt hàng lần nào

```sql
SELECT
    ProductCode,
    ProductName,
    Brand,
    UnitPrice,
    StockQuantity
FROM Products
WHERE ProductId NOT IN (
    SELECT DISTINCT ProductId
    FROM OrderDetails
);
```

---

### 2.9 Subquery in SELECT

**English:** Show products with their total quantity ordered

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    p.UnitPrice,
    p.StockQuantity,
    (SELECT SUM(od.Quantity)
     FROM OrderDetails od
     WHERE od.ProductId = p.ProductId) AS TotalOrdered
FROM Products p;
```

**Vietnamese:** Hiển thị sản phẩm với tổng số lượng đã đặt

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    p.UnitPrice,
    p.StockQuantity,
    (SELECT SUM(od.Quantity)
     FROM OrderDetails od
     WHERE od.ProductId = p.ProductId) AS TotalOrdered
FROM Products p;
```

---

### 2.10 DISTINCT

**English:** Get all unique cities where customers live

```sql
SELECT DISTINCT City
FROM Customers;
```

**Vietnamese:** Lấy tất cả các thành phố khách hàng sinh sống

```sql
SELECT DISTINCT City
FROM Customers;
```

---

### 2.11 CASE Expression

**English:** Categorize products by price range

```sql
SELECT
    ProductCode,
    ProductName,
    UnitPrice,
    CASE
        WHEN UnitPrice < 500000 THEN N'Giá rẻ'
        WHEN UnitPrice < 10000000 THEN N'Trang trung'
        ELSE N'Cao cấp'
    END AS PriceCategory
FROM Products;
```

**Vietnamese:** Phân loại sản phẩm theo mức giá

```sql
SELECT
    ProductCode,
    ProductName,
    UnitPrice,
    CASE
        WHEN UnitPrice < 500000 THEN N'Giá rẻ'
        WHEN UnitPrice < 10000000 THEN N'Trang trung'
        ELSE N'Cao cấp'
    END AS PriceCategory
FROM Products;
```

---

### 2.12 Self JOIN

**English:** Show employees with their managers

```sql
SELECT
    e.EmployeeCode,
    e.FullName AS EmployeeName,
    e.Position,
    m.FullName AS ManagerName,
    m.Position AS ManagerPosition
FROM Employees e
LEFT JOIN Employees m ON e.ManagerId = m.EmployeeId;
```

**Vietnamese:** Hiển thị nhân viên và quản lý trực tiếp của họ

```sql
SELECT
    e.EmployeeCode,
    e.FullName AS EmployeeName,
    e.Position,
    m.FullName AS ManagerName,
    m.Position AS ManagerPosition
FROM Employees e
LEFT JOIN Employees m ON e.ManagerId = m.EmployeeId;
```

---

### 2.13 Aggregation with JOIN

**English:** Calculate revenue by product

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    SUM(od.Quantity) AS TotalQuantitySold,
    SUM(od.LineTotal) AS TotalRevenue
FROM Products p
INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
GROUP BY p.ProductCode, p.ProductName;
```

**Vietnamese:** Tính doanh thu theo sản phẩm

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    SUM(od.Quantity) AS TotalQuantitySold,
    SUM(od.LineTotal) AS TotalRevenue
FROM Products p
INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
GROUP BY p.ProductCode, p.ProductName;
```

---

## Level 3: Advanced Queries (Nâng cao)

### 3.1 Window Functions - ROW_NUMBER

**English:** Rank customers by total spending

```sql
SELECT
    CustomerCode,
    FullName,
    City,
    TotalSpent,
    ROW_NUMBER() OVER (ORDER BY TotalSpent DESC) AS Ranking
FROM Customers
WHERE TotalSpent > 0;
```

**Vietnamese:** Xếp hạng khách hàng theo tổng chi tiêu

```sql
SELECT
    CustomerCode,
    FullName,
    City,
    TotalSpent,
    ROW_NUMBER() OVER (ORDER BY TotalSpent DESC) AS Ranking
FROM Customers
WHERE TotalSpent > 0;
```

---

### 3.2 Window Functions - RANK and DENSE_RANK

**English:** Rank products by sales quantity with ties

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    SUM(od.Quantity) AS TotalSold,
    RANK() OVER (ORDER BY SUM(od.Quantity) DESC) AS Rank,
    DENSE_RANK() OVER (ORDER BY SUM(od.Quantity) DESC) AS DenseRank
FROM Products p
INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
GROUP BY p.ProductCode, p.ProductName;
```

**Vietnamese:** Xếp hạng sản phẩm theo số lượng bán với các liên kết

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    SUM(od.Quantity) AS TotalSold,
    RANK() OVER (ORDER BY SUM(od.Quantity) DESC) AS Rank,
    DENSE_RANK() OVER (ORDER BY SUM(od.Quantity) DESC) AS DenseRank
FROM Products p
INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
GROUP BY p.ProductCode, p.ProductName;
```

---

### 3.3 Window Functions - PARTITION BY

**English:** Show each customer's order number

```sql
SELECT
    o.OrderCode,
    c.CustomerCode,
    c.FullName,
    o.OrderDate,
    o.TotalAmount,
    ROW_NUMBER() OVER (PARTITION BY c.CustomerId ORDER BY o.OrderDate) AS OrderNumber
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.CustomerId;
```

**Vietnamese:** Hiển thị số thứ tự đơn hàng của từng khách hàng

```sql
SELECT
    o.OrderCode,
    c.CustomerCode,
    c.FullName,
    o.OrderDate,
    o.TotalAmount,
    ROW_NUMBER() OVER (PARTITION BY c.CustomerId ORDER BY o.OrderDate) AS OrderNumber
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.CustomerId;
```

---

### 3.4 Window Functions - Running Total

**English:** Calculate running total of order amounts

```sql
SELECT
    OrderCode,
    OrderDate,
    TotalAmount,
    SUM(TotalAmount) OVER (ORDER BY OrderDate) AS RunningTotal,
    SUM(TotalAmount) OVER (ORDER BY OrderDate ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningTotalAlt
FROM Orders
WHERE Status = 'Delivered';
```

**Vietnamese:** Tính tổng tích lũy của các đơn hàng

```sql
SELECT
    OrderCode,
    OrderDate,
    TotalAmount,
    SUM(TotalAmount) OVER (ORDER BY OrderDate) AS RunningTotal,
    SUM(TotalAmount) OVER (ORDER BY OrderDate ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningTotalAlt
FROM Orders
WHERE Status = 'Delivered';
```

---

### 3.5 Window Functions - Moving Average

**English:** Calculate 3-month moving average of revenue

```sql
SELECT
    YEAR(OrderDate) AS Year,
    MONTH(OrderDate) AS Month,
    SUM(TotalAmount) AS MonthlyRevenue,
    AVG(SUM(TotalAmount)) OVER (
        ORDER BY YEAR(OrderDate), MONTH(OrderDate)
        ROWS BETWEEN 2 PRECEDING AND CURRENT ROW
    ) AS MovingAverage3Month
FROM Orders
WHERE Status = 'Delivered'
GROUP BY YEAR(OrderDate), MONTH(OrderDate);
```

**Vietnamese:** Tính trung bình động 3 tháng của doanh thu

```sql
SELECT
    YEAR(OrderDate) AS Year,
    MONTH(OrderDate) AS Month,
    SUM(TotalAmount) AS MonthlyRevenue,
    AVG(SUM(TotalAmount)) OVER (
        ORDER BY YEAR(OrderDate), MONTH(OrderDate)
        ROWS BETWEEN 2 PRECEDING AND CURRENT ROW
    ) AS MovingAverage3Month
FROM Orders
WHERE Status = 'Delivered'
GROUP BY YEAR(OrderDate), MONTH(OrderDate);
```

---

### 3.6 CTE - Common Table Expression

**English:** Find top 3 selling products using CTE

```sql
WITH ProductSales AS (
    SELECT
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        SUM(od.Quantity) AS TotalSold,
        SUM(od.LineTotal) AS TotalRevenue
    FROM Products p
    INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
    GROUP BY p.ProductId, p.ProductCode, p.ProductName
)
SELECT TOP 3
    ProductCode,
    ProductName,
    TotalSold,
    TotalRevenue
FROM ProductSales
ORDER BY TotalRevenue DESC;
```

**Vietnamese:** Tìm 3 sản phẩm bán chạy nhất sử dụng CTE

```sql
WITH ProductSales AS (
    SELECT
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        SUM(od.Quantity) AS TotalSold,
        SUM(od.LineTotal) AS TotalRevenue
    FROM Products p
    INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
    GROUP BY p.ProductId, p.ProductCode, p.ProductName
)
SELECT TOP 3
    ProductCode,
    ProductName,
    TotalSold,
    TotalRevenue
FROM ProductSales
ORDER BY TotalRevenue DESC;
```

---

### 3.7 Multiple CTEs

**English:** Analyze customer purchase patterns with multiple CTEs

```sql
WITH CustomerOrders AS (
    SELECT
        CustomerId,
        COUNT(OrderId) AS OrderCount,
        SUM(TotalAmount) AS TotalSpent
    FROM Orders
    GROUP BY CustomerId
),
CustomerReviews AS (
    SELECT
        CustomerId,
        COUNT(ReviewId) AS ReviewCount,
        AVG(Rating) AS AvgRating
    FROM ProductReviews
    GROUP BY CustomerId
)
SELECT
    c.CustomerCode,
    c.FullName,
    c.CustomerType,
    COALESCE(co.OrderCount, 0) AS TotalOrders,
    COALESCE(co.TotalSpent, 0) AS LifetimeValue,
    COALESCE(cr.ReviewCount, 0) AS TotalReviews,
    COALESCE(cr.AvgRating, 0) AS AverageRating
FROM Customers c
LEFT JOIN CustomerOrders co ON c.CustomerId = co.CustomerId
LEFT JOIN CustomerReviews cr ON c.CustomerId = cr.CustomerId;
```

**Vietnamese:** Phân tích mô hình mua hàng của khách hàng

```sql
WITH CustomerOrders AS (
    SELECT
        CustomerId,
        COUNT(OrderId) AS OrderCount,
        SUM(TotalAmount) AS TotalSpent
    FROM Orders
    GROUP BY CustomerId
),
CustomerReviews AS (
    SELECT
        CustomerId,
        COUNT(ReviewId) AS ReviewCount,
        AVG(Rating) AS AvgRating
    FROM ProductReviews
    GROUP BY CustomerId
)
SELECT
    c.CustomerCode,
    c.FullName,
    c.CustomerType,
    COALESCE(co.OrderCount, 0) AS TotalOrders,
    COALESCE(co.TotalSpent, 0) AS LifetimeValue,
    COALESCE(cr.ReviewCount, 0) AS TotalReviews,
    COALESCE(cr.AvgRating, 0) AS AverageRating
FROM Customers c
LEFT JOIN CustomerOrders co ON c.CustomerId = co.CustomerId
LEFT JOIN CustomerReviews cr ON c.CustomerId = cr.CustomerId;
```

---

### 3.8 Correlated Subquery

**English:** Find products that are above average price in their category

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    p.CategoryId,
    p.UnitPrice,
    (SELECT AVG(UnitPrice) FROM Products WHERE CategoryId = p.CategoryId) AS CategoryAvgPrice
FROM Products p
WHERE p.UnitPrice > (
    SELECT AVG(UnitPrice)
    FROM Products
    WHERE CategoryId = p.CategoryId
);
```

**Vietnamese:** Tìm sản phẩm có giá cao hơn giá trung bình trong danh mục

```sql
SELECT
    p.ProductCode,
    p.ProductName,
    p.CategoryId,
    p.UnitPrice,
    (SELECT AVG(UnitPrice) FROM Products WHERE CategoryId = p.CategoryId) AS CategoryAvgPrice
FROM Products p
WHERE p.UnitPrice > (
    SELECT AVG(UnitPrice)
    FROM Products
    WHERE CategoryId = p.CategoryId
);
```

---

### 3.9 EXISTS Operator

**English:** Find customers who have ordered iPhone 15

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    c.Email,
    c.Phone
FROM Customers c
WHERE EXISTS (
    SELECT 1
    FROM Orders o
    INNER JOIN OrderDetails od ON o.OrderId = od.OrderId
    INNER JOIN Products p ON od.ProductId = p.ProductId
    WHERE o.CustomerId = c.CustomerId
    AND p.ProductName LIKE '%iPhone 15%'
);
```

**Vietnamese:** Tìm khách hàng đã đặt mua iPhone 15

```sql
SELECT
    c.CustomerCode,
    c.FullName,
    c.Email,
    c.Phone
FROM Customers c
WHERE EXISTS (
    SELECT 1
    FROM Orders o
    INNER JOIN OrderDetails od ON o.OrderId = od.OrderId
    INNER JOIN Products p ON od.ProductId = p.ProductId
    WHERE o.CustomerId = c.CustomerId
    AND p.ProductName LIKE N'%iPhone 15%'
);
```

---

### 3.10 Complex JOIN with Aggregation

**English:** Get detailed order summary with product details

```sql
SELECT
    o.OrderCode,
    c.FullName AS CustomerName,
    e.FullName AS SalesPerson,
    o.OrderDate,
    COUNT(od.OrderDetailId) AS ItemCount,
    SUM(od.Quantity) AS TotalQuantity,
    o.SubTotal,
    o.DiscountAmount,
    o.TaxAmount,
    o.ShippingFee,
    o.TotalAmount,
    o.Status,
    o.PaymentMethod
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.CustomerId
INNER JOIN Employees e ON o.EmployeeId = e.EmployeeId
INNER JOIN OrderDetails od ON o.OrderId = od.OrderId
GROUP BY
    o.OrderCode,
    c.FullName,
    e.FullName,
    o.OrderDate,
    o.SubTotal,
    o.DiscountAmount,
    o.TaxAmount,
    o.ShippingFee,
    o.TotalAmount,
    o.Status,
    o.PaymentMethod;
```

**Vietnamese:** Lấy tóm tắt chi tiết đơn hàng

```sql
SELECT
    o.OrderCode,
    c.FullName AS CustomerName,
    e.FullName AS SalesPerson,
    o.OrderDate,
    COUNT(od.OrderDetailId) AS ItemCount,
    SUM(od.Quantity) AS TotalQuantity,
    o.SubTotal,
    o.DiscountAmount,
    o.TaxAmount,
    o.ShippingFee,
    o.TotalAmount,
    o.Status,
    o.PaymentMethod
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.CustomerId
INNER JOIN Employees e ON o.EmployeeId = e.EmployeeId
INNER JOIN OrderDetails od ON o.OrderId = od.OrderId
GROUP BY
    o.OrderCode,
    c.FullName,
    e.FullName,
    o.OrderDate,
    o.SubTotal,
    o.DiscountAmount,
    o.TaxAmount,
    o.ShippingFee,
    o.TotalAmount,
    o.Status,
    o.PaymentMethod;
```

---

### 3.11 Hierarchical Query - Categories

**English:** Show category hierarchy

```sql
SELECT
    c1.CategoryCode AS ParentCode,
    c1.CategoryName AS ParentName,
    c2.CategoryCode AS ChildCode,
    c2.CategoryName AS ChildName
FROM Categories c1
INNER JOIN Categories c2 ON c1.CategoryId = c2.ParentCategoryId
ORDER BY c1.DisplayOrder, c2.DisplayOrder;
```

**Vietnamese:** Hiển thị phân cấp danh mục

```sql
SELECT
    c1.CategoryCode AS ParentCode,
    c1.CategoryName AS ParentName,
    c2.CategoryCode AS ChildCode,
    c2.CategoryName AS ChildName
FROM Categories c1
INNER JOIN Categories c2 ON c1.CategoryId = c2.ParentCategoryId
ORDER BY c1.DisplayOrder, c2.DisplayOrder;
```

---

### 3.12 PIVOT / Cross Tab

**English:** Show order count by status and payment method

```sql
SELECT
    PaymentMethod,
    SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END) AS Pending,
    SUM(CASE WHEN Status = 'Processing' THEN 1 ELSE 0 END) AS Processing,
    SUM(CASE WHEN Status = 'Shipped' THEN 1 ELSE 0 END) AS Shipped,
    SUM(CASE WHEN Status = 'Delivered' THEN 1 ELSE 0 END) AS Delivered,
    SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END) AS Cancelled,
    COUNT(*) AS Total
FROM Orders
GROUP BY PaymentMethod;
```

**Vietnamese:** Hiển thị số đơn hàng theo trạng thái và phương thức thanh toán

```sql
SELECT
    PaymentMethod,
    SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END) AS Pending,
    SUM(CASE WHEN Status = 'Processing' THEN 1 ELSE 0 END) AS Processing,
    SUM(CASE WHEN Status = 'Shipped' THEN 1 ELSE 0 END) AS Shipped,
    SUM(CASE WHEN Status = 'Delivered' THEN 1 ELSE 0 END) AS Delivered,
    SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END) AS Cancelled,
    COUNT(*) AS Total
FROM Orders
GROUP BY PaymentMethod;
```

---

### 3.13 Complex Analysis - Year-over-Year Comparison

**English:** Compare revenue between 2023 and 2024

```sql
WITH RevenueByYear AS (
    SELECT
        YEAR(OrderDate) AS Year,
        MONTH(OrderDate) AS Month,
        SUM(TotalAmount) AS MonthlyRevenue
    FROM Orders
    WHERE Status = 'Delivered'
    GROUP BY YEAR(OrderDate), MONTH(OrderDate)
)
SELECT
    r1.Month,
    r1.MonthlyRevenue AS Revenue2023,
    r2.MonthlyRevenue AS Revenue2024,
    r2.MonthlyRevenue - r1.MonthlyRevenue AS YoYGrowth,
    CASE
        WHEN r1.MonthlyRevenue > 0
        THEN (r2.MonthlyRevenue - r1.MonthlyRevenue) * 100.0 / r1.MonthlyRevenue
        ELSE 0
    END AS YoYGrowthPercent
FROM RevenueByYear r1
INNER JOIN RevenueByYear r2 ON r1.Month = r2.Month AND r2.Year = 2024
WHERE r1.Year = 2023
ORDER BY r1.Month;
```

**Vietnamese:** So sánh doanh thu giữa năm 2023 và 2024

```sql
WITH RevenueByYear AS (
    SELECT
        YEAR(OrderDate) AS Year,
        MONTH(OrderDate) AS Month,
        SUM(TotalAmount) AS MonthlyRevenue
    FROM Orders
    WHERE Status = 'Delivered'
    GROUP BY YEAR(OrderDate), MONTH(OrderDate)
)
SELECT
    r1.Month,
    r1.MonthlyRevenue AS Revenue2023,
    r2.MonthlyRevenue AS Revenue2024,
    r2.MonthlyRevenue - r1.MonthlyRevenue AS YoYGrowth,
    CASE
        WHEN r1.MonthlyRevenue > 0
        THEN (r2.MonthlyRevenue - r1.MonthlyRevenue) * 100.0 / r1.MonthlyRevenue
        ELSE 0
    END AS YoYGrowthPercent
FROM RevenueByYear r1
INNER JOIN RevenueByYear r2 ON r1.Month = r2.Month AND r2.Year = 2024
WHERE r1.Year = 2023
ORDER BY r1.Month;
```

---

### 3.14 Inventory Analysis

**English:** Calculate stock turnover rate

```sql
WITH ProductMetrics AS (
    SELECT
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        p.StockQuantity AS CurrentStock,
        p.MinStockLevel,
        (SELECT SUM(Quantity) FROM InventoryTransactions
         WHERE ProductId = p.ProductId AND TransactionType = 'In') AS TotalIn,
        (SELECT SUM(Quantity) FROM InventoryTransactions
         WHERE ProductId = p.ProductId AND TransactionType = 'Out') AS TotalOut
    FROM Products p
)
SELECT
    ProductCode,
    ProductName,
    CurrentStock,
    TotalIn,
    TotalOut,
    CASE
        WHEN TotalIn > 0 THEN CAST(TotalOut AS FLOAT) / TotalIn * 100
        ELSE 0
    END AS TurnoverRate,
    CASE
        WHEN CurrentStock < MinStockLevel THEN N'Cần nhập hàng'
        ELSE N'Đủ hàng'
    END AS StockStatus
FROM ProductMetrics;
```

**Vietnamese:** Phân tích tồn kho và tỷ lệ luân chuyển

```sql
WITH ProductMetrics AS (
    SELECT
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        p.StockQuantity AS CurrentStock,
        p.MinStockLevel,
        (SELECT SUM(Quantity) FROM InventoryTransactions
         WHERE ProductId = p.ProductId AND TransactionType = 'In') AS TotalIn,
        (SELECT SUM(Quantity) FROM InventoryTransactions
         WHERE ProductId = p.ProductId AND TransactionType = 'Out') AS TotalOut
    FROM Products p
)
SELECT
    ProductCode,
    ProductName,
    CurrentStock,
    TotalIn,
    TotalOut,
    CASE
        WHEN TotalIn > 0 THEN CAST(TotalOut AS FLOAT) / TotalIn * 100
        ELSE 0
    END AS TurnoverRate,
    CASE
        WHEN CurrentStock < MinStockLevel THEN N'Cần nhập hàng'
        ELSE N'Đủ hàng'
    END AS StockStatus
FROM ProductMetrics;
```

---

### 3.15 Customer Lifetime Value Analysis

**English:** Calculate customer lifetime value with predictions

```sql
WITH CustomerMetrics AS (
    SELECT
        c.CustomerId,
        c.CustomerCode,
        c.FullName,
        c.CustomerType,
        c.RegistrationDate,
        COUNT(o.OrderId) AS TotalOrders,
        SUM(o.TotalAmount) AS TotalSpent,
        AVG(o.TotalAmount) AS AvgOrderValue,
        MAX(o.OrderDate) AS LastOrderDate,
        DATEDIFF(DAY, MIN(o.OrderDate), MAX(o.OrderDate)) AS CustomerTenureDays
    FROM Customers c
    LEFT JOIN Orders o ON c.CustomerId = o.CustomerId
    GROUP BY c.CustomerId, c.CustomerCode, c.FullName, c.CustomerType, c.RegistrationDate
)
SELECT
    CustomerCode,
    FullName,
    CustomerType,
    RegistrationDate,
    TotalOrders,
    TotalSpent,
    AvgOrderValue,
    LastOrderDate,
    CustomerTenureDays,
    CASE
        WHEN CustomerTenureDays > 0
        THEN TotalSpent / (CustomerTenureDays / 30.0)
        ELSE 0
    END AS MonthlySpent,
    CASE
        WHEN TotalOrders > 0
        THEN TotalSpent / TotalOrders
        ELSE 0
    END AS LifetimeValuePerOrder
FROM CustomerMetrics
WHERE TotalOrders > 0
ORDER BY TotalSpent DESC;
```

**Vietnamese:** Phân tích giá trị vòng đời khách hàng

```sql
WITH CustomerMetrics AS (
    SELECT
        c.CustomerId,
        c.CustomerCode,
        c.FullName,
        c.CustomerType,
        c.RegistrationDate,
        COUNT(o.OrderId) AS TotalOrders,
        SUM(o.TotalAmount) AS TotalSpent,
        AVG(o.TotalAmount) AS AvgOrderValue,
        MAX(o.OrderDate) AS LastOrderDate,
        DATEDIFF(DAY, MIN(o.OrderDate), MAX(o.OrderDate)) AS CustomerTenureDays
    FROM Customers c
    LEFT JOIN Orders o ON c.CustomerId = o.CustomerId
    GROUP BY c.CustomerId, c.CustomerCode, c.FullName, c.CustomerType, c.RegistrationDate
)
SELECT
    CustomerCode,
    FullName,
    CustomerType,
    RegistrationDate,
    TotalOrders,
    TotalSpent,
    AvgOrderValue,
    LastOrderDate,
    CustomerTenureDays,
    CASE
        WHEN CustomerTenureDays > 0
        THEN TotalSpent / (CustomerTenureDays / 30.0)
        ELSE 0
    END AS MonthlySpent,
    CASE
        WHEN TotalOrders > 0
        THEN TotalSpent / TotalOrders
        ELSE 0
    END AS LifetimeValuePerOrder
FROM CustomerMetrics
WHERE TotalOrders > 0
ORDER BY TotalSpent DESC;
```

---

### 3.16 Product Performance Score

**English:** Calculate comprehensive product performance score

```sql
WITH ProductStats AS (
    SELECT
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        p.UnitPrice,
        p.StockQuantity,
        SUM(od.Quantity) AS TotalSold,
        SUM(od.LineTotal) AS TotalRevenue,
        COUNT(DISTINCT od.OrderId) AS OrderCount,
        AVG(od.UnitPrice) AS AvgSellingPrice
    FROM Products p
    LEFT JOIN OrderDetails od ON p.ProductId = od.ProductId
    GROUP BY p.ProductId, p.ProductCode, p.ProductName, p.UnitPrice, p.StockQuantity
),
ReviewStats AS (
    SELECT
        ProductId,
        AVG(CAST(Rating AS FLOAT)) AS AvgRating,
        COUNT(ReviewId) AS ReviewCount
    FROM ProductReviews
    GROUP BY ProductId
)
SELECT
    ps.ProductCode,
    ps.ProductName,
    ps.UnitPrice,
    ps.StockQuantity,
    COALESCE(ps.TotalSold, 0) AS TotalSold,
    COALESCE(ps.TotalRevenue, 0) AS TotalRevenue,
    COALESCE(ps.OrderCount, 0) AS OrderCount,
    COALESCE(rs.AvgRating, 0) AS AvgRating,
    COALESCE(rs.ReviewCount, 0) AS ReviewCount,
    CASE
        WHEN ps.TotalRevenue > 50000000 THEN N'Bestseller'
        WHEN ps.TotalRevenue > 20000000 THEN N'High Performer'
        WHEN ps.TotalRevenue > 5000000 THEN N'Average'
        WHEN ps.TotalRevenue > 0 THEN N'Low Performer'
        ELSE N'No Sales'
    END AS PerformanceTier
FROM ProductStats ps
LEFT JOIN ReviewStats rs ON ps.ProductId = rs.ProductId
ORDER BY ps.TotalRevenue DESC;
```

**Vietnamese:** Tính điểm hiệu suất sản phẩm

```sql
WITH ProductStats AS (
    SELECT
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        p.UnitPrice,
        p.StockQuantity,
        SUM(od.Quantity) AS TotalSold,
        SUM(od.LineTotal) AS TotalRevenue,
        COUNT(DISTINCT od.OrderId) AS OrderCount,
        AVG(od.UnitPrice) AS AvgSellingPrice
    FROM Products p
    LEFT JOIN OrderDetails od ON p.ProductId = od.ProductId
    GROUP BY p.ProductId, p.ProductCode, p.ProductName, p.UnitPrice, p.StockQuantity
),
ReviewStats AS (
    SELECT
        ProductId,
        AVG(CAST(Rating AS FLOAT)) AS AvgRating,
        COUNT(ReviewId) AS ReviewCount
    FROM ProductReviews
    GROUP BY ProductId
)
SELECT
    ps.ProductCode,
    ps.ProductName,
    ps.UnitPrice,
    ps.StockQuantity,
    COALESCE(ps.TotalSold, 0) AS TotalSold,
    COALESCE(ps.TotalRevenue, 0) AS TotalRevenue,
    COALESCE(ps.OrderCount, 0) AS OrderCount,
    COALESCE(rs.AvgRating, 0) AS AvgRating,
    COALESCE(rs.ReviewCount, 0) AS ReviewCount,
    CASE
        WHEN ps.TotalRevenue > 50000000 THEN N'Bestseller'
        WHEN ps.TotalRevenue > 20000000 THEN N'High Performer'
        WHEN ps.TotalRevenue > 5000000 THEN N'Average'
        WHEN ps.TotalRevenue > 0 THEN N'Low Performer'
        ELSE N'No Sales'
    END AS PerformanceTier
FROM ProductStats ps
LEFT JOIN ReviewStats rs ON ps.ProductId = rs.ProductId
ORDER BY ps.TotalRevenue DESC;
```

---

## Quick Reference - Table Fields

### Customers Table

| Field            | Type          | Description           |
| ---------------- | ------------- | --------------------- |
| CustomerId       | INT           | Primary key           |
| CustomerCode     | NVARCHAR(20)  | Unique customer code  |
| FullName         | NVARCHAR(100) | Customer full name    |
| Email            | NVARCHAR(100) | Email address         |
| Phone            | NVARCHAR(20)  | Phone number          |
| DateOfBirth      | DATE          | Date of birth         |
| Gender           | NVARCHAR(10)  | Gender                |
| City             | NVARCHAR(50)  | City                  |
| District         | NVARCHAR(50)  | District              |
| Address          | NVARCHAR(200) | Address               |
| CustomerType     | NVARCHAR(20)  | Regular, VIP, Premium |
| RegistrationDate | DATETIME      | Registration date     |
| LastPurchaseDate | DATETIME      | Last purchase date    |
| TotalSpent       | DECIMAL(18,2) | Total amount spent    |
| IsActive         | BIT           | Active status         |

### Products Table

| Field         | Type          | Description               |
| ------------- | ------------- | ------------------------- |
| ProductId     | INT           | Primary key               |
| ProductCode   | NVARCHAR(20)  | Unique product code       |
| ProductName   | NVARCHAR(200) | Product name              |
| CategoryId    | INT           | Foreign key to Categories |
| Brand         | NVARCHAR(50)  | Brand name                |
| UnitPrice     | DECIMAL(18,2) | Selling price             |
| CostPrice     | DECIMAL(18,2) | Cost price                |
| StockQuantity | INT           | Current stock             |
| MinStockLevel | INT           | Minimum stock threshold   |
| Unit          | NVARCHAR(20)  | Unit of measure           |

### Orders Table

| Field          | Type          | Description              |
| -------------- | ------------- | ------------------------ |
| OrderId        | INT           | Primary key              |
| OrderCode      | NVARCHAR(20)  | Unique order code        |
| CustomerId     | INT           | Foreign key to Customers |
| EmployeeId     | INT           | Foreign key to Employees |
| OrderDate      | DATETIME      | Order date               |
| RequiredDate   | DATETIME      | Required delivery date   |
| ShippedDate    | DATETIME      | Shipped date             |
| ShipCity       | NVARCHAR(50)  | Shipping city            |
| SubTotal       | DECIMAL(18,2) | Subtotal                 |
| DiscountAmount | DECIMAL(18,2) | Discount amount          |
| TaxAmount      | DECIMAL(18,2) | Tax amount               |
| ShippingFee    | DECIMAL(18,2) | Shipping fee             |
| TotalAmount    | DECIMAL(18,2) | Total amount             |
| Status         | NVARCHAR(20)  | Order status             |
| PaymentMethod  | NVARCHAR(20)  | Payment method           |
| PaymentStatus  | NVARCHAR(20)  | Payment status           |

### OrderDetails Table

| Field           | Type          | Description             |
| --------------- | ------------- | ----------------------- |
| OrderDetailId   | INT           | Primary key             |
| OrderId         | INT           | Foreign key to Orders   |
| ProductId       | INT           | Foreign key to Products |
| Quantity        | INT           | Quantity ordered        |
| UnitPrice       | DECIMAL(18,2) | Unit price              |
| DiscountPercent | DECIMAL(5,2)  | Discount percent        |
| DiscountAmount  | DECIMAL(18,2) | Discount amount         |
| TaxPercent      | DECIMAL(5,2)  | Tax percent             |
| TaxAmount       | DECIMAL(18,2) | Tax amount              |
| LineTotal       | DECIMAL(18,2) | Line total              |

### Employees Table

| Field        | Type          | Description           |
| ------------ | ------------- | --------------------- |
| EmployeeId   | INT           | Primary key           |
| EmployeeCode | NVARCHAR(20)  | Unique employee code  |
| FullName     | NVARCHAR(100) | Employee full name    |
| Email        | NVARCHAR(100) | Email address         |
| Phone        | NVARCHAR(20)  | Phone number          |
| Position     | NVARCHAR(50)  | Position              |
| Department   | NVARCHAR(50)  | Department            |
| ManagerId    | INT           | Manager's employee ID |
| HireDate     | DATE          | Hire date             |
| Salary       | DECIMAL(18,2) | Salary                |

### Categories Table

| Field            | Type          | Description          |
| ---------------- | ------------- | -------------------- |
| CategoryId       | INT           | Primary key          |
| CategoryCode     | NVARCHAR(20)  | Unique category code |
| CategoryName     | NVARCHAR(100) | Category name        |
| ParentCategoryId | INT           | Parent category ID   |
| Description      | NVARCHAR(500) | Description          |
| DisplayOrder     | INT           | Display order        |
| IsActive         | BIT           | Active status        |

### Suppliers Table

| Field         | Type          | Description          |
| ------------- | ------------- | -------------------- |
| SupplierId    | INT           | Primary key          |
| SupplierCode  | NVARCHAR(20)  | Unique supplier code |
| SupplierName  | NVARCHAR(200) | Supplier name        |
| ContactPerson | NVARCHAR(100) | Contact person       |
| Email         | NVARCHAR(100) | Email address        |
| Phone         | NVARCHAR(20)  | Phone number         |
| Address       | NVARCHAR(200) | Address              |
| City          | NVARCHAR(50)  | City                 |
| TaxCode       | NVARCHAR(20)  | Tax code             |
| IsActive      | BIT           | Active status        |

### Promotions Table

| Field             | Type          | Description           |
| ----------------- | ------------- | --------------------- |
| PromotionId       | INT           | Primary key           |
| PromotionCode     | NVARCHAR(20)  | Unique promotion code |
| PromotionName     | NVARCHAR(200) | Promotion name        |
| Description       | NVARCHAR(500) | Description           |
| DiscountType      | NVARCHAR(20)  | Percent or Amount     |
| DiscountValue     | DECIMAL(18,2) | Discount value        |
| MinOrderAmount    | DECIMAL(18,2) | Minimum order amount  |
| MaxDiscountAmount | DECIMAL(18,2) | Maximum discount      |
| StartDate         | DATETIME      | Start date            |
| EndDate           | DATETIME      | End date              |
| IsActive          | BIT           | Active status         |

### ProductReviews Table

| Field              | Type           | Description              |
| ------------------ | -------------- | ------------------------ |
| ReviewId           | INT            | Primary key              |
| ProductId          | INT            | Foreign key to Products  |
| CustomerId         | INT            | Foreign key to Customers |
| OrderId            | INT            | Foreign key to Orders    |
| Rating             | INT            | Rating (1-5)             |
| ReviewText         | NVARCHAR(1000) | Review text              |
| ReviewDate         | DATETIME       | Review date              |
| IsVerifiedPurchase | BIT            | Verified purchase flag   |

### InventoryTransactions Table

| Field           | Type          | Description             |
| --------------- | ------------- | ----------------------- |
| TransactionId   | INT           | Primary key             |
| ProductId       | INT           | Foreign key to Products |
| TransactionType | NVARCHAR(20)  | In, Out, Adjustment     |
| Quantity        | INT           | Transaction quantity    |
| ReferenceType   | NVARCHAR(20)  | Reference type          |
| ReferenceId     | INT           | Reference ID            |
| Notes           | NVARCHAR(200) | Notes                   |
| TransactionDate | DATETIME      | Transaction date        |
| CreatedBy       | INT           | Employee ID             |

---

_Generated for TextToSqlTest Database Testing_
