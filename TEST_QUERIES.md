# Test Queries for Text-to-SQL System

Hướng dẫn: Copy câu hỏi từ cột "Natural Language Query" và dán vào chat interface để test hệ thống.

---

## 1. QUERY - Simple (SELECT cơ bản)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Show me all customers" | QUERY | Select tất cả records từ bảng customers |
| 2 | "List all products" | QUERY | Liệt kê tất cả sản phẩm |
| 3 | "Get all orders" | QUERY | Lấy danh sách đơn hàng |
| 4 | "Show me the users table" | QUERY | Xem dữ liệu bảng users |
| 5 | "What employees do we have?" | QUERY | Hiển thị danh sách nhân viên |

---

## 2. QUERY - Medium (WHERE, ORDER BY, LIMIT)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Show me customers from Hanoi" | QUERY | Select với điều kiện location |
| 2 | "List orders from last month" | QUERY | Lọc theo thời gian |
| 3 | "Get top 10 most expensive products" | QUERY | Sort và limit |
| 4 | "Show me active users ordered by created date" | QUERY | Kết hợp WHERE và ORDER BY |
| 5 | "Find customers who placed more than 5 orders" | QUERY | Điều kiện count |
| 6 | "List products with price between 100 and 500" | QUERY | BETWEEN clause |
| 7 | "Show me orders that are pending" | QUERY | Lọc theo status |

---

## 3. QUERY - Complex (JOIN, GROUP BY, Subquery)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Show me all orders with customer names" | QUERY | JOIN orders với customers |
| 2 | "List products with their categories" | QUERY | JOIN products với categories |
| 3 | "Get total revenue by month" | QUERY | GROUP BY với aggregate |
| 4 | "Show customer names with their order count" | QUERY | LEFT JOIN + GROUP BY + COUNT |
| 5 | "Find products that have never been ordered" | QUERY | NOT IN subquery |
| 6 | "Show top 5 customers by total spending" | QUERY | JOIN + GROUP BY + ORDER BY + LIMIT |
| 7 | "List suppliers who provide more than 3 products" | QUERY | GROUP BY + HAVING |
| 8 | "Get average order value by customer age group" | QUERY | JOIN + GROUP BY với calculation |

---

## 4. INSERT (Thêm dữ liệu mới)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Add a new customer named John Smith" | INSERT | Insert một record mới |
| 2 | "Create a new product called Laptop" | INSERT | Thêm sản phẩm |
| 3 | "Insert a new order for customer ID 5" | INSERT | Tạo đơn hàng |
| 4 | "Register a new user with email test@test.com" | INSERT | Đăng ký user |
| 5 | "Thêm khách hàng mới tên Nguyễn Văn A" | INSERT | Vietnamese - thêm KH mới |

---

## 5. UPDATE (Cập nhật dữ liệu)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Update customer email to newemail@test.com" | UPDATE | Cập nhật email |
| 2 | "Change order status to shipped" | UPDATE | Đổi status đơn hàng |
| 3 | "Update product price to 999" | UPDATE | Cập nhật giá sản phẩm |
| 4 | "Cập nhật địa chỉ khách hàng thành Hà Nội" | UPDATE | Vietnamese - cập nhật địa chỉ |
| 5 | "Sửa tên sản phẩm thành iPhone 15" | UPDATE | Vietnamese - sửa tên |

---

## 6. DDL - INDEX (Tạo index)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Create index on customer email" | DDL_INDEX | Tạo index cho cột |
| 2 | "Add index for order date" | DDL_INDEX | Thêm index |
| 3 | "Tạo index cho cột phone trong bảng customers" | DDL_INDEX | Vietnamese - tạo index |
| 4 | "Drop index on product name" | DDL_INDEX | Xóa index |

---

## 7. DDL - ALTER TABLE (Thêm/xóa cột)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Add a new column phone to customers table" | DDL_ALTER | Thêm cột mới |
| 2 | "Add age column to users table" | DDL_ALTER | Thêm cột age |
| 3 | "Thêm cột address vào bảng customers" | DDL_ALTER | Vietnamese - thêm cột |
| 4 | "Drop column temporary from orders" | DDL_ALTER | Xóa cột |
| 5 | "Modify column type for price" | DDL_ALTER | Đổi kiểu dữ liệu |

---

## 8. DDL - VIEW (Tạo view)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Create a view for active customers" | DDL_VIEW | Tạo view đơn giản |
| 2 | "Create view showing order totals" | DDL_VIEW | View với calculation |
| 3 | "Tạo view liệt kê đơn hàng đã hoàn thành" | DDL_VIEW | Vietnamese - tạo view |

---

## 9. DDL - PROCEDURE/FUNCTION (Stored Procedure)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Create a procedure to calculate order total" | DDL_PROCEDURE | Tạo stored procedure |
| 2 | "Create function to get customer age" | DDL_PROCEDURE | Tạo function |
| 3 | "Tạo procedure tính tổng doanh thu" | DDL_PROCEDURE | Vietnamese - tạo procedure |

---

## 10. FORBIDDEN - Delete/Drop Operations (Sẽ bị chặn!)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "Delete customer with ID 123" | FORBIDDEN | ❌ Bị chặn - hard delete |
| 2 | "Delete all orders" | FORBIDDEN | ❌ Bị chặn - xóa tất cả |
| 3 | "Drop table users" | FORBIDDEN | ❌ Bị chặn - drop table |
| 4 | "Remove this customer" | FORBIDDEN | ❌ Bị chặn - remove |
| 5 | "Xóa khách hàng" | FORBIDDEN | ❌ Bị chặn - Vietnamese |
| 6 | "Clear all data from orders" | FORBIDDEN | ❌ Bị chặn - clear |
| 7 | "Truncate table products" | FORBIDDEN | ❌ Bị chặn - truncate |

---

## 11. OFF_TOPIC (Không liên quan đến database)

| # | Natural Language Query | Expected Intent | Description |
|---|----------------------|-----------------|-------------|
| 1 | "What's the weather today?" | OFF_TOPIC | Không liên quan DB |
| 2 | "Hello, how are you?" | OFF_TOPIC | Greeting |
| 3 | "Tell me a joke" | OFF_TOPIC | Không liên quan |

---

## Sample Test Scenarios

### Scenario 1: Query Flow
```
User: "Show me all customers"
System: SELECT * FROM customers

User: "Filter to those in Hanoi"
System: SELECT * FROM customers WHERE city = 'Hanoi'

User: "Show their orders"
System: SELECT * FROM orders WHERE customer_id IN (...)
```

### Scenario 2: Write Flow
```
User: "Add a new customer John Doe"
System: INSERT INTO customers (name, ...) VALUES ('John Doe', ...)
→ Hiển thị confirmation modal
User: Confirm
System: Execute INSERT
```

### Scenario 3: Forbidden Flow
```
User: "Delete customer 123"
System: ⚠️ BLOCKED
→ Hiển thị alternatives:
  - Soft Delete: UPDATE customers SET status = 'inactive' WHERE id = 123
  - Archive: INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123
```
