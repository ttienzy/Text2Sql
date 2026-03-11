-- P1-07: Integration test database initialization script
-- Creates test database with sample data for reproducible tests

USE master;
GO

-- Create test database if not exists
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TextToSqlTest')
BEGIN
    CREATE DATABASE TextToSqlTest;
END
GO

USE TextToSqlTest;
GO

-- Create test user
IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = 'TextToSqlReader')
BEGIN
    CREATE LOGIN TextToSqlReader WITH PASSWORD = 'Reader@2024!Strong';
END
GO

IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = 'TextToSqlReader')
BEGIN
    CREATE USER TextToSqlReader FOR LOGIN TextToSqlReader;
    ALTER ROLE db_datareader ADD MEMBER TextToSqlReader;
    ALTER ROLE db_datawriter ADD MEMBER TextToSqlReader;
END
GO

-- Drop existing tables if they exist
DROP TABLE IF EXISTS OrderDetails;
DROP TABLE IF EXISTS Orders;
DROP TABLE IF EXISTS Products;
DROP TABLE IF EXISTS Categories;
DROP TABLE IF EXISTS Customers;
GO

-- Create Customers table
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY IDENTITY(1,1),
    FullName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100),
    Phone NVARCHAR(20),
    City NVARCHAR(50),
    Country NVARCHAR(50),
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- Create Categories table
CREATE TABLE Categories (
    CategoryId INT PRIMARY KEY IDENTITY(1,1),
    CategoryName NVARCHAR(50) NOT NULL,
    Description NVARCHAR(200)
);
GO

-- Create Products table
CREATE TABLE Products (
    ProductId INT PRIMARY KEY IDENTITY(1,1),
    ProductName NVARCHAR(100) NOT NULL,
    CategoryId INT FOREIGN KEY REFERENCES Categories(CategoryId),
    UnitPrice DECIMAL(10,2) NOT NULL,
    UnitsInStock INT DEFAULT 0,
    Discontinued BIT DEFAULT 0
);
GO

-- Create Orders table
CREATE TABLE Orders (
    OrderId INT PRIMARY KEY IDENTITY(1,1),
    CustomerId INT FOREIGN KEY REFERENCES Customers(CustomerId),
    OrderDate DATETIME2 DEFAULT GETDATE(),
    ShipDate DATETIME2,
    TotalAmount DECIMAL(10,2),
    Status NVARCHAR(20) DEFAULT 'Pending'
);
GO

-- Create OrderDetails table
CREATE TABLE OrderDetails (
    OrderDetailId INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT FOREIGN KEY REFERENCES Orders(OrderId),
    ProductId INT FOREIGN KEY REFERENCES Products(ProductId),
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    Discount DECIMAL(3,2) DEFAULT 0
);
GO

-- Insert sample data: Customers
INSERT INTO Customers (FullName, Email, Phone, City, Country) VALUES
('Nguyễn Văn An', 'an.nguyen@example.com', '0901234567', 'Hà Nội', 'Vietnam'),
('Trần Thị Bình', 'binh.tran@example.com', '0912345678', 'Hồ Chí Minh', 'Vietnam'),
('Lê Văn Cường', 'cuong.le@example.com', '0923456789', 'Đà Nẵng', 'Vietnam'),
('Phạm Thị Dung', 'dung.pham@example.com', '0934567890', 'Hà Nội', 'Vietnam'),
('Hoàng Văn Em', 'em.hoang@example.com', '0945678901', 'Cần Thơ', 'Vietnam'),
('Vũ Thị Phương', 'phuong.vu@example.com', '0956789012', 'Hải Phòng', 'Vietnam'),
('Đỗ Văn Giang', 'giang.do@example.com', '0967890123', 'Hà Nội', 'Vietnam'),
('Bùi Thị Hoa', 'hoa.bui@example.com', '0978901234', 'Huế', 'Vietnam'),
('Đinh Văn Inh', 'inh.dinh@example.com', '0989012345', 'Nha Trang', 'Vietnam'),
('Mai Thị Kim', 'kim.mai@example.com', '0990123456', 'Hồ Chí Minh', 'Vietnam');
GO

-- Insert sample data: Categories
INSERT INTO Categories (CategoryName, Description) VALUES
('Electronics', 'Electronic devices and accessories'),
('Clothing', 'Fashion and apparel'),
('Books', 'Books and publications'),
('Home & Garden', 'Home improvement and garden supplies'),
('Sports', 'Sports equipment and accessories');
GO

-- Insert sample data: Products
INSERT INTO Products (ProductName, CategoryId, UnitPrice, UnitsInStock, Discontinued) VALUES
('Laptop Dell XPS 13', 1, 25000000, 15, 0),
('iPhone 15 Pro', 1, 30000000, 20, 0),
('Samsung Galaxy S24', 1, 22000000, 25, 0),
('Áo sơ mi nam', 2, 350000, 100, 0),
('Quần jean nữ', 2, 450000, 80, 0),
('Sách lập trình C#', 3, 250000, 50, 0),
('Sách AI & Machine Learning', 3, 350000, 30, 0),
('Bàn làm việc', 4, 2500000, 10, 0),
('Ghế văn phòng', 4, 1800000, 15, 0),
('Giày chạy bộ Nike', 5, 2200000, 40, 0);
GO

-- Insert sample data: Orders
INSERT INTO Orders (CustomerId, OrderDate, ShipDate, TotalAmount, Status) VALUES
(1, '2024-01-15', '2024-01-17', 25000000, 'Delivered'),
(2, '2024-01-20', '2024-01-22', 30000000, 'Delivered'),
(3, '2024-02-01', '2024-02-03', 22000000, 'Delivered'),
(4, '2024-02-10', NULL, 800000, 'Pending'),
(5, '2024-02-15', '2024-02-17', 4300000, 'Delivered'),
(1, '2024-03-01', '2024-03-03', 600000, 'Delivered'),
(2, '2024-03-05', NULL, 2200000, 'Processing'),
(6, '2024-03-10', '2024-03-12', 1800000, 'Delivered'),
(7, '2024-03-15', NULL, 350000, 'Pending'),
(8, '2024-03-20', '2024-03-22', 25000000, 'Delivered');
GO

-- Insert sample data: OrderDetails
INSERT INTO OrderDetails (OrderId, ProductId, Quantity, UnitPrice, Discount) VALUES
(1, 1, 1, 25000000, 0),
(2, 2, 1, 30000000, 0),
(3, 3, 1, 22000000, 0),
(4, 4, 2, 350000, 0.1),
(4, 5, 1, 450000, 0),
(5, 8, 1, 2500000, 0),
(5, 9, 1, 1800000, 0),
(6, 6, 2, 250000, 0),
(6, 7, 1, 350000, 0),
(7, 10, 1, 2200000, 0),
(8, 9, 1, 1800000, 0),
(9, 4, 1, 350000, 0),
(10, 1, 1, 25000000, 0);
GO

-- Create indexes for better query performance
CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
CREATE INDEX IX_Orders_OrderDate ON Orders(OrderDate);
CREATE INDEX IX_OrderDetails_OrderId ON OrderDetails(OrderId);
CREATE INDEX IX_OrderDetails_ProductId ON OrderDetails(ProductId);
CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);
GO

-- Grant permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO TextToSqlReader;
GO

PRINT 'Test database initialized successfully!';
PRINT 'Database: TextToSqlTest';
PRINT 'Tables: Customers (10 rows), Categories (5 rows), Products (10 rows), Orders (10 rows), OrderDetails (13 rows)';
GO
