-- =============================================
-- Text To SQL Agent - Test Database Setup
-- =============================================

-- Drop if exists
IF EXISTS (SELECT * FROM sys.databases WHERE name = 'TextToSqlTest')
BEGIN
    ALTER DATABASE TextToSqlTest SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE TextToSqlTest;
END
GO

-- Create database
CREATE DATABASE TextToSqlTest;
GO

USE TextToSqlTest;
GO

-- =============================================
-- Table: Customers
-- =============================================
CREATE TABLE Customers (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100),
    Phone NVARCHAR(20),
    City NVARCHAR(50),
    CreatedDate DATETIME DEFAULT GETDATE()
);

-- =============================================
-- Table: Categories
-- =============================================
CREATE TABLE Categories (
    Id INT PRIMARY KEY IDENTITY(1,1),
    CategoryName NVARCHAR(50) NOT NULL,
    Description NVARCHAR(200)
);

-- =============================================
-- Table: Products
-- =============================================
CREATE TABLE Products (
    Id INT PRIMARY KEY IDENTITY(1,1),
    ProductName NVARCHAR(100) NOT NULL,
    CategoryId INT FOREIGN KEY REFERENCES Categories(Id),
    Price DECIMAL(18,2) NOT NULL,
    Stock INT DEFAULT 0,
    CreatedDate DATETIME DEFAULT GETDATE()
);

-- =============================================
-- Table: Orders
-- =============================================
CREATE TABLE Orders (
    Id INT PRIMARY KEY IDENTITY(1,1),
    CustomerId INT FOREIGN KEY REFERENCES Customers(Id),
    OrderDate DATETIME DEFAULT GETDATE(),
    TotalAmount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(20) DEFAULT 'Pending'
);

-- =============================================
-- Table: OrderDetails
-- =============================================
CREATE TABLE OrderDetails (
    Id INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT FOREIGN KEY REFERENCES Orders(Id),
    ProductId INT FOREIGN KEY REFERENCES Products(Id),
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL
);

-- =============================================
-- Sample Data
-- =============================================

-- Customers
INSERT INTO Customers (Name, Email, Phone, City) VALUES
('Nguyễn Văn A', 'nguyenvana@email.com', '0901234567', 'Hà Nội'),
('Trần Thị B', 'tranthib@email.com', '0912345678', 'Hồ Chí Minh'),
('Lê Văn C', 'levanc@email.com', '0923456789', 'Đà Nẵng'),
('Phạm Thị D', 'phamthid@email.com', '0934567890', 'Hà Nội'),
('Hoàng Văn E', 'hoangvane@email.com', '0945678901', 'Cần Thơ');

-- Categories
INSERT INTO Categories (CategoryName, Description) VALUES
('Điện tử', 'Thiết bị điện tử'),
('Thời trang', 'Quần áo, phụ kiện'),
('Thực phẩm', 'Đồ ăn, đồ uống'),
('Nội thất', 'Đồ dùng gia đình');

-- Products
INSERT INTO Products (ProductName, CategoryId, Price, Stock) VALUES
('Laptop Dell XPS 13', 1, 25000000, 10),
('iPhone 15 Pro', 1, 30000000, 15),
('Áo sơ mi nam', 2, 350000, 50),
('Quần jean nữ', 2, 450000, 30),
('Cà phê hạt nguyên chất', 3, 150000, 100),
('Bàn làm việc gỗ', 4, 2500000, 5);

-- Orders
INSERT INTO Orders (CustomerId, OrderDate, TotalAmount, Status) VALUES
(1, DATEADD(DAY, -5, GETDATE()), 25000000, 'Completed'),
(2, DATEADD(DAY, -3, GETDATE()), 800000, 'Completed'),
(3, DATEADD(DAY, -2, GETDATE()), 30000000, 'Pending'),
(1, DATEADD(DAY, -1, GETDATE()), 150000, 'Completed'),
(4, GETDATE(), 2500000, 'Pending');

-- OrderDetails
INSERT INTO OrderDetails (OrderId, ProductId, Quantity, UnitPrice) VALUES
(1, 1, 1, 25000000),
(2, 3, 2, 350000),
(2, 5, 1, 150000),
(3, 2, 1, 30000000),
(4, 5, 1, 150000),
(5, 6, 1, 2500000);

GO

-- =============================================
-- Create Read-Only User
-- =============================================
CREATE LOGIN TextToSqlReader WITH PASSWORD = '@TextToSqlReader!';
GO

USE TextToSqlTest;
GO

CREATE USER TextToSqlReader FOR LOGIN TextToSqlReader;
GO

-- Grant SELECT only
GRANT SELECT ON SCHEMA::dbo TO TextToSqlReader;
GO

-- Verify
SELECT 
    dp.name AS UserName,
    dp.type_desc AS UserType,
    o.name AS ObjectName,
    p.permission_name,
    p.state_desc
FROM sys.database_permissions p
JOIN sys.database_principals dp ON p.grantee_principal_id = dp.principal_id
LEFT JOIN sys.objects o ON p.major_id = o.object_id
WHERE dp.name = 'TextToSqlReader';
GO

PRINT 'Database setup completed successfully!';
PRINT 'Connection String: Server=.;Database=TextToSqlTest;User Id=TextToSqlReader;Password=@TextToSqlReader!;TrustServerCertificate=True;';