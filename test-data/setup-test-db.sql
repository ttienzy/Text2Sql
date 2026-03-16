-- =============================================
-- Text To SQL Agent - Advanced Test Database
-- User: sa / Password: 123
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
-- CORE TABLES
-- =============================================

-- Customers (Khách hàng)
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY IDENTITY(1,1),
    CustomerCode NVARCHAR(20) UNIQUE NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100),
    Phone NVARCHAR(20),
    DateOfBirth DATE,
    Gender NVARCHAR(10),
    City NVARCHAR(50),
    District NVARCHAR(50),
    Address NVARCHAR(200),
    CustomerType NVARCHAR(20) DEFAULT 'Regular', -- Regular, VIP, Premium
    RegistrationDate DATETIME DEFAULT GETDATE(),
    LastPurchaseDate DATETIME,
    TotalSpent DECIMAL(18,2) DEFAULT 0,
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE(),
    UpdatedAt DATETIME DEFAULT GETDATE()
);

-- Categories (Danh mục sản phẩm)
CREATE TABLE Categories (
    CategoryId INT PRIMARY KEY IDENTITY(1,1),
    CategoryCode NVARCHAR(20) UNIQUE NOT NULL,
    CategoryName NVARCHAR(100) NOT NULL,
    ParentCategoryId INT NULL FOREIGN KEY REFERENCES Categories(CategoryId),
    Description NVARCHAR(500),
    DisplayOrder INT DEFAULT 0,
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- Products (Sản phẩm)
CREATE TABLE Products (
    ProductId INT PRIMARY KEY IDENTITY(1,1),
    ProductCode NVARCHAR(20) UNIQUE NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    CategoryId INT FOREIGN KEY REFERENCES Categories(CategoryId),
    Brand NVARCHAR(50),
    UnitPrice DECIMAL(18,2) NOT NULL,
    CostPrice DECIMAL(18,2),
    StockQuantity INT DEFAULT 0,
    MinStockLevel INT DEFAULT 10,
    Unit NVARCHAR(20) DEFAULT N'Cái',
    Description NVARCHAR(1000),
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE(),
    UpdatedAt DATETIME DEFAULT GETDATE()
);

-- Suppliers (Nhà cung cấp)
CREATE TABLE Suppliers (
    SupplierId INT PRIMARY KEY IDENTITY(1,1),
    SupplierCode NVARCHAR(20) UNIQUE NOT NULL,
    SupplierName NVARCHAR(200) NOT NULL,
    ContactPerson NVARCHAR(100),
    Email NVARCHAR(100),
    Phone NVARCHAR(20),
    Address NVARCHAR(200),
    City NVARCHAR(50),
    TaxCode NVARCHAR(20),
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- ProductSuppliers (Sản phẩm - Nhà cung cấp, many-to-many)
CREATE TABLE ProductSuppliers (
    ProductSupplierId INT PRIMARY KEY IDENTITY(1,1),
    ProductId INT FOREIGN KEY REFERENCES Products(ProductId),
    SupplierId INT FOREIGN KEY REFERENCES Suppliers(SupplierId),

    SupplyPrice DECIMAL(18,2),
    LeadTimeDays INT,
    IsPreferred BIT DEFAULT 0,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- Employees (Nhân viên)
CREATE TABLE Employees (
    EmployeeId INT PRIMARY KEY IDENTITY(1,1),
    EmployeeCode NVARCHAR(20) UNIQUE NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100),
    Phone NVARCHAR(20),
    Position NVARCHAR(50),
    Department NVARCHAR(50),
    ManagerId INT NULL FOREIGN KEY REFERENCES Employees(EmployeeId),
    HireDate DATE,
    Salary DECIMAL(18,2),
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- Orders (Đơn hàng)
CREATE TABLE Orders (
    OrderId INT PRIMARY KEY IDENTITY(1,1),
    OrderCode NVARCHAR(20) UNIQUE NOT NULL,
    CustomerId INT FOREIGN KEY REFERENCES Customers(CustomerId),
    EmployeeId INT FOREIGN KEY REFERENCES Employees(EmployeeId),
    OrderDate DATETIME DEFAULT GETDATE(),
    RequiredDate DATETIME,
    ShippedDate DATETIME,
    ShipAddress NVARCHAR(200),
    ShipCity NVARCHAR(50),
    ShipDistrict NVARCHAR(50),
    SubTotal DECIMAL(18,2) DEFAULT 0,
    DiscountAmount DECIMAL(18,2) DEFAULT 0,
    TaxAmount DECIMAL(18,2) DEFAULT 0,
    ShippingFee DECIMAL(18,2) DEFAULT 0,
    TotalAmount DECIMAL(18,2) DEFAULT 0,
    Status NVARCHAR(20) DEFAULT 'Pending', -- Pending, Processing, Shipped, Delivered, Cancelled
    PaymentMethod NVARCHAR(20), -- Cash, Card, Transfer, COD
    PaymentStatus NVARCHAR(20) DEFAULT 'Unpaid', -- Unpaid, Partial, Paid
    Notes NVARCHAR(500),
    CreatedAt DATETIME DEFAULT GETDATE(),
    UpdatedAt DATETIME DEFAULT GETDATE()
);

-- OrderDetails (Chi tiết đơn hàng)
CREATE TABLE OrderDetails (
    OrderDetailId INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT FOREIGN KEY REFERENCES Orders(OrderId),
    ProductId INT FOREIGN KEY REFERENCES Products(ProductId),
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    DiscountPercent DECIMAL(5,2) DEFAULT 0,
    DiscountAmount DECIMAL(18,2) DEFAULT 0,
    TaxPercent DECIMAL(5,2) DEFAULT 10,
    TaxAmount DECIMAL(18,2) DEFAULT 0,
    LineTotal DECIMAL(18,2) NOT NULL,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- Promotions (Khuyến mãi)
CREATE TABLE Promotions (
    PromotionId INT PRIMARY KEY IDENTITY(1,1),
    PromotionCode NVARCHAR(20) UNIQUE NOT NULL,
    PromotionName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(500),
    DiscountType NVARCHAR(20), -- Percent, Amount
    DiscountValue DECIMAL(18,2),
    MinOrderAmount DECIMAL(18,2) DEFAULT 0,
    MaxDiscountAmount DECIMAL(18,2),
    StartDate DATETIME,
    EndDate DATETIME,
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- OrderPromotions (Đơn hàng - Khuyến mãi)
CREATE TABLE OrderPromotions (
    OrderPromotionId INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT FOREIGN KEY REFERENCES Orders(OrderId),
    PromotionId INT FOREIGN KEY REFERENCES Promotions(PromotionId),
    DiscountAmount DECIMAL(18,2),
    AppliedAt DATETIME DEFAULT GETDATE()
);

-- Inventory (Lịch sử tồn kho)
CREATE TABLE InventoryTransactions (
    TransactionId INT PRIMARY KEY IDENTITY(1,1),
    ProductId INT FOREIGN KEY REFERENCES Products(ProductId),
    TransactionType NVARCHAR(20), -- In, Out, Adjustment
    Quantity INT NOT NULL,
    ReferenceType NVARCHAR(20), -- Order, Purchase, Adjustment
    ReferenceId INT,
    Notes NVARCHAR(200),
    TransactionDate DATETIME DEFAULT GETDATE(),
    CreatedBy INT FOREIGN KEY REFERENCES Employees(EmployeeId)
);

-- Reviews (Đánh giá sản phẩm)
CREATE TABLE ProductReviews (
    ReviewId INT PRIMARY KEY IDENTITY(1,1),
    ProductId INT FOREIGN KEY REFERENCES Products(ProductId),
    CustomerId INT FOREIGN KEY REFERENCES Customers(CustomerId),
    OrderId INT FOREIGN KEY REFERENCES Orders(OrderId),
    Rating INT CHECK (Rating BETWEEN 1 AND 5),
    ReviewText NVARCHAR(1000),
    ReviewDate DATETIME DEFAULT GETDATE(),
    IsVerifiedPurchase BIT DEFAULT 0
);

-- =============================================
-- INDEXES for Performance
-- =============================================
CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
CREATE INDEX IX_Orders_OrderDate ON Orders(OrderDate);
CREATE INDEX IX_Orders_Status ON Orders(Status);
CREATE INDEX IX_OrderDetails_OrderId ON OrderDetails(OrderId);
CREATE INDEX IX_OrderDetails_ProductId ON OrderDetails(ProductId);
CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);
CREATE INDEX IX_Customers_City ON Customers(City);
CREATE INDEX IX_Customers_CustomerType ON Customers(CustomerType);

GO


-- =============================================
-- SAMPLE DATA
-- =============================================

-- Categories (Hierarchical)
INSERT INTO Categories (CategoryCode, CategoryName, ParentCategoryId, DisplayOrder) VALUES
('ELEC', N'Điện tử', NULL, 1),
('ELEC-PHONE', N'Điện thoại', 1, 1),
('ELEC-LAPTOP', N'Laptop', 1, 2),
('ELEC-TABLET', N'Máy tính bảng', 1, 3),
('FASHION', N'Thời trang', NULL, 2),
('FASHION-MEN', N'Thời trang nam', 5, 1),
('FASHION-WOMEN', N'Thời trang nữ', 5, 2),
('FOOD', N'Thực phẩm', NULL, 3),
('FOOD-DRINK', N'Đồ uống', 8, 1),
('FOOD-SNACK', N'Đồ ăn vặt', 8, 2),
('HOME', N'Nội thất', NULL, 4),
('HOME-FURNITURE', N'Đồ gỗ', 11, 1),
('HOME-DECOR', N'Trang trí', 11, 2);

-- Suppliers
INSERT INTO Suppliers (SupplierCode, SupplierName, ContactPerson, Email, Phone, City) VALUES
('SUP001', N'Công ty TNHH Điện tử ABC', N'Nguyễn Văn A', 'abc@supplier.com', '0281234567', N'Hà Nội'),
('SUP002', N'Công ty CP Thời trang XYZ', N'Trần Thị B', 'xyz@supplier.com', '0282345678', N'Hồ Chí Minh'),
('SUP003', N'Nhà phân phối Thực phẩm DEF', N'Lê Văn C', 'def@supplier.com', '0283456789', N'Đà Nẵng'),
('SUP004', N'Công ty Nội thất GHI', N'Phạm Thị D', 'ghi@supplier.com', '0284567890', N'Hà Nội');

-- Products
INSERT INTO Products (ProductCode, ProductName, CategoryId, Brand, UnitPrice, CostPrice, StockQuantity, MinStockLevel) VALUES
-- Electronics
('PHONE001', N'iPhone 15 Pro Max 256GB', 2, 'Apple', 32990000, 28000000, 25, 5),
('PHONE002', N'Samsung Galaxy S24 Ultra', 2, 'Samsung', 29990000, 25000000, 30, 5),
('PHONE003', N'Xiaomi 14 Pro', 2, 'Xiaomi', 19990000, 16000000, 40, 10),
('LAPTOP001', N'MacBook Pro 14 M3', 3, 'Apple', 45990000, 40000000, 15, 3),
('LAPTOP002', N'Dell XPS 15', 3, 'Dell', 35990000, 30000000, 20, 5),
('LAPTOP003', N'Lenovo ThinkPad X1', 3, 'Lenovo', 32990000, 28000000, 18, 5),
('TABLET001', N'iPad Pro 12.9', 4, 'Apple', 28990000, 25000000, 12, 3),
-- Fashion
('SHIRT001', N'Áo sơ mi nam công sở', 6, 'Pierre Cardin', 450000, 300000, 100, 20),
('SHIRT002', N'Áo thun nam basic', 6, 'Uniqlo', 199000, 120000, 150, 30),
('DRESS001', N'Váy công sở nữ', 7, 'Zara', 890000, 600000, 80, 15),
('JEANS001', N'Quần jean nam slim fit', 6, 'Levi''s', 1290000, 900000, 60, 15),
-- Food
('COFFEE001', N'Cà phê hạt Arabica 500g', 9, 'Trung Nguyên', 180000, 120000, 200, 50),
('COFFEE002', N'Cà phê phin Robusta 500g', 9, 'Highlands', 150000, 100000, 250, 50),
('TEA001', N'Trà xanh Thái Nguyên 200g', 9, 'Phúc Long', 120000, 80000, 180, 40),
('SNACK001', N'Bánh quy bơ 200g', 10, 'Kinh Đô', 45000, 30000, 300, 100),
-- Home
('DESK001', N'Bàn làm việc gỗ sồi', 12, 'Nội Thất Hòa Phát', 3500000, 2800000, 15, 3),
('CHAIR001', N'Ghế văn phòng ergonomic', 12, 'Nội Thất Hòa Phát', 2200000, 1800000, 20, 5),
('LAMP001', N'Đèn bàn LED', 13, 'Philips', 450000, 300000, 50, 10);

-- ProductSuppliers
INSERT INTO ProductSuppliers (ProductId, SupplierId, SupplyPrice, LeadTimeDays, IsPreferred) VALUES
(1, 1, 28000000, 7, 1), (2, 1, 25000000, 7, 1), (3, 1, 16000000, 5, 0),
(4, 1, 40000000, 10, 1), (5, 1, 30000000, 7, 0), (6, 1, 28000000, 7, 0),
(8, 2, 300000, 3, 1), (9, 2, 120000, 2, 1), (10, 2, 600000, 5, 1), (11, 2, 900000, 5, 1),
(12, 3, 120000, 2, 1), (13, 3, 100000, 2, 1), (14, 3, 80000, 3, 1), (15, 3, 30000, 1, 1),
(16, 4, 2800000, 14, 1), (17, 4, 1800000, 10, 1), (18, 4, 300000, 5, 1);

-- Employees
INSERT INTO Employees (EmployeeCode, FullName, Email, Phone, Position, Department, ManagerId, HireDate, Salary) VALUES
('EMP001', N'Nguyễn Văn Quản', 'quannv@company.com', '0901111111', N'Giám đốc', N'Ban Giám đốc', NULL, '2020-01-01', 50000000),
('EMP002', N'Trần Thị Lan', 'lantt@company.com', '0902222222', N'Trưởng phòng Kinh doanh', N'Kinh doanh', 1, '2020-06-01', 30000000),
('EMP003', N'Lê Văn Hùng', 'hunglv@company.com', '0903333333', N'Nhân viên Kinh doanh', N'Kinh doanh', 2, '2021-03-15', 15000000),
('EMP004', N'Phạm Thị Mai', 'maipt@company.com', '0904444444', N'Nhân viên Kinh doanh', N'Kinh doanh', 2, '2021-06-01', 15000000),
('EMP005', N'Hoàng Văn Nam', 'namhv@company.com', '0905555555', N'Trưởng phòng Kho', N'Kho vận', 1, '2020-03-01', 25000000),
('EMP006', N'Đỗ Thị Hoa', 'hoadt@company.com', '0906666666', N'Nhân viên Kho', N'Kho vận', 5, '2021-09-01', 12000000);

-- Customers
INSERT INTO Customers (CustomerCode, FullName, Email, Phone, DateOfBirth, Gender, City, District, CustomerType, RegistrationDate) VALUES
('CUS001', N'Nguyễn Văn An', 'an.nguyen@email.com', '0911111111', '1990-05-15', N'Nam', N'Hà Nội', N'Cầu Giấy', 'VIP', '2023-01-15'),
('CUS002', N'Trần Thị Bình', 'binh.tran@email.com', '0912222222', '1985-08-20', N'Nữ', N'Hồ Chí Minh', N'Quận 1', 'Premium', '2023-02-20'),
('CUS003', N'Lê Văn Cường', 'cuong.le@email.com', '0913333333', '1992-03-10', N'Nam', N'Đà Nẵng', N'Hải Châu', 'Regular', '2023-03-10'),
('CUS004', N'Phạm Thị Dung', 'dung.pham@email.com', '0914444444', '1988-11-25', N'Nữ', N'Hà Nội', N'Đống Đa', 'VIP', '2023-04-05'),
('CUS005', N'Hoàng Văn Em', 'em.hoang@email.com', '0915555555', '1995-07-30', N'Nam', N'Cần Thơ', N'Ninh Kiều', 'Regular', '2023-05-12'),
('CUS006', N'Đỗ Thị Phương', 'phuong.do@email.com', '0916666666', '1991-12-05', N'Nữ', N'Hà Nội', N'Ba Đình', 'Premium', '2023-06-18'),
('CUS007', N'Vũ Văn Giang', 'giang.vu@email.com', '0917777777', '1987-04-18', N'Nam', N'Hải Phòng', N'Lê Chân', 'Regular', '2023-07-22'),
('CUS008', N'Bùi Thị Hương', 'huong.bui@email.com', '0918888888', '1993-09-08', N'Nữ', N'Hồ Chí Minh', N'Quận 3', 'VIP', '2023-08-30'),
('CUS009', N'Đinh Văn Ích', 'ich.dinh@email.com', '0919999999', '1989-02-14', N'Nam', N'Hà Nội', N'Hoàng Mai', 'Regular', '2023-09-15'),
('CUS010', N'Ngô Thị Kim', 'kim.ngo@email.com', '0910101010', '1994-06-22', N'Nữ', N'Đà Nẵng', N'Thanh Khê', 'Premium', '2023-10-20');

-- Promotions
INSERT INTO Promotions (PromotionCode, PromotionName, Description, DiscountType, DiscountValue, MinOrderAmount, MaxDiscountAmount, StartDate, EndDate, IsActive) VALUES
('SUMMER2024', N'Khuyến mãi hè 2024', N'Giảm 10% cho đơn hàng trên 5 triệu', 'Percent', 10, 5000000, 1000000, '2024-06-01', '2024-08-31', 1),
('NEWCUST', N'Khách hàng mới', N'Giảm 500k cho khách hàng mới', 'Amount', 500000, 2000000, 500000, '2024-01-01', '2024-12-31', 1),
('VIP2024', N'Ưu đãi VIP', N'Giảm 15% cho khách VIP', 'Percent', 15, 10000000, 3000000, '2024-01-01', '2024-12-31', 1);

GO


-- Orders (2023 - 2024 data for time-series analysis)
DECLARE @StartDate DATETIME = '2023-01-01';
DECLARE @OrderCounter INT = 1;

-- Q1 2023
INSERT INTO Orders (OrderCode, CustomerId, EmployeeId, OrderDate, ShipCity, SubTotal, TaxAmount, ShippingFee, TotalAmount, Status, PaymentMethod, PaymentStatus) VALUES
('ORD' + RIGHT('00000' + CAST(@OrderCounter AS VARCHAR), 5), 1, 3, DATEADD(DAY, 5, @StartDate), N'Hà Nội', 32990000, 3299000, 0, 36289000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 1 AS VARCHAR), 5), 2, 3, DATEADD(DAY, 12, @StartDate), N'Hồ Chí Minh', 45990000, 4599000, 50000, 50639000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 2 AS VARCHAR), 5), 3, 4, DATEADD(DAY, 20, @StartDate), N'Đà Nẵng', 890000, 89000, 30000, 1009000, 'Delivered', 'COD', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 3 AS VARCHAR), 5), 4, 3, DATEADD(DAY, 35, @StartDate), N'Hà Nội', 6200000, 620000, 0, 6820000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 4 AS VARCHAR), 5), 1, 4, DATEADD(DAY, 45, @StartDate), N'Hà Nội', 450000, 45000, 20000, 515000, 'Delivered', 'Cash', 'Paid'),

-- Q2 2023
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 5 AS VARCHAR), 5), 5, 3, DATEADD(DAY, 95, @StartDate), N'Cần Thơ', 29990000, 2999000, 50000, 33039000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 6 AS VARCHAR), 5), 6, 4, DATEADD(DAY, 105, @StartDate), N'Hà Nội', 35990000, 3599000, 0, 39589000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 7 AS VARCHAR), 5), 2, 3, DATEADD(DAY, 120, @StartDate), N'Hồ Chí Minh', 1740000, 174000, 30000, 1944000, 'Delivered', 'COD', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 8 AS VARCHAR), 5), 7, 4, DATEADD(DAY, 135, @StartDate), N'Hải Phòng', 19990000, 1999000, 40000, 22029000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 9 AS VARCHAR), 5), 8, 3, DATEADD(DAY, 150, @StartDate), N'Hồ Chí Minh', 28990000, 2899000, 0, 31889000, 'Delivered', 'Card', 'Paid'),

-- Q3 2023
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 10 AS VARCHAR), 5), 3, 4, DATEADD(DAY, 185, @StartDate), N'Đà Nẵng', 32990000, 3299000, 30000, 36319000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 11 AS VARCHAR), 5), 9, 3, DATEADD(DAY, 200, @StartDate), N'Hà Nội', 5700000, 570000, 0, 6270000, 'Delivered', 'Cash', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 12 AS VARCHAR), 5), 4, 4, DATEADD(DAY, 215, @StartDate), N'Hà Nội', 45990000, 4599000, 0, 50589000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 13 AS VARCHAR), 5), 10, 3, DATEADD(DAY, 230, @StartDate), N'Đà Nẵng', 2090000, 209000, 30000, 2329000, 'Delivered', 'COD', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 14 AS VARCHAR), 5), 1, 4, DATEADD(DAY, 245, @StartDate), N'Hà Nội', 180000, 18000, 20000, 218000, 'Delivered', 'Cash', 'Paid'),

-- Q4 2023
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 15 AS VARCHAR), 5), 2, 3, DATEADD(DAY, 280, @StartDate), N'Hồ Chí Minh', 32990000, 3299000, 50000, 36339000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 16 AS VARCHAR), 5), 6, 4, DATEADD(DAY, 295, @StartDate), N'Hà Nội', 29990000, 2999000, 0, 32989000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 17 AS VARCHAR), 5), 5, 3, DATEADD(DAY, 310, @StartDate), N'Cần Thơ', 1290000, 129000, 40000, 1459000, 'Delivered', 'COD', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 18 AS VARCHAR), 5), 8, 4, DATEADD(DAY, 325, @StartDate), N'Hồ Chí Minh', 35990000, 3599000, 0, 39589000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 19 AS VARCHAR), 5), 7, 3, DATEADD(DAY, 340, @StartDate), N'Hải Phòng', 450000, 45000, 30000, 525000, 'Delivered', 'Cash', 'Paid'),

-- Q1 2024
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 20 AS VARCHAR), 5), 1, 3, DATEADD(DAY, 370, @StartDate), N'Hà Nội', 45990000, 4599000, 0, 50589000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 21 AS VARCHAR), 5), 3, 4, DATEADD(DAY, 385, @StartDate), N'Đà Nẵng', 28990000, 2899000, 30000, 31919000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 22 AS VARCHAR), 5), 4, 3, DATEADD(DAY, 400, @StartDate), N'Hà Nội', 32990000, 3299000, 0, 36289000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 23 AS VARCHAR), 5), 9, 4, DATEADD(DAY, 415, @StartDate), N'Hà Nội', 2200000, 220000, 0, 2420000, 'Delivered', 'Cash', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 24 AS VARCHAR), 5), 2, 3, DATEADD(DAY, 430, @StartDate), N'Hồ Chí Minh', 19990000, 1999000, 50000, 22039000, 'Delivered', 'COD', 'Paid'),

-- Q2 2024
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 25 AS VARCHAR), 5), 10, 4, DATEADD(DAY, 460, @StartDate), N'Đà Nẵng', 35990000, 3599000, 30000, 39619000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 26 AS VARCHAR), 5), 6, 3, DATEADD(DAY, 475, @StartDate), N'Hà Nội', 29990000, 2999000, 0, 32989000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 27 AS VARCHAR), 5), 5, 4, DATEADD(DAY, 490, @StartDate), N'Cần Thơ', 1740000, 174000, 40000, 1954000, 'Delivered', 'COD', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 28 AS VARCHAR), 5), 8, 3, DATEADD(DAY, 505, @StartDate), N'Hồ Chí Minh', 45990000, 4599000, 0, 50589000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 29 AS VARCHAR), 5), 7, 4, DATEADD(DAY, 520, @StartDate), N'Hải Phòng', 32990000, 3299000, 30000, 36319000, 'Delivered', 'Card', 'Paid'),

-- Recent orders (last 30 days)
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 30 AS VARCHAR), 5), 1, 3, DATEADD(DAY, -25, GETDATE()), N'Hà Nội', 28990000, 2899000, 0, 31889000, 'Delivered', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 31 AS VARCHAR), 5), 2, 4, DATEADD(DAY, -20, GETDATE()), N'Hồ Chí Minh', 35990000, 3599000, 50000, 39639000, 'Delivered', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 32 AS VARCHAR), 5), 3, 3, DATEADD(DAY, -15, GETDATE()), N'Đà Nẵng', 890000, 89000, 30000, 1009000, 'Delivered', 'COD', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 33 AS VARCHAR), 5), 4, 4, DATEADD(DAY, -10, GETDATE()), N'Hà Nội', 45990000, 4599000, 0, 50589000, 'Shipped', 'Transfer', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 34 AS VARCHAR), 5), 5, 3, DATEADD(DAY, -5, GETDATE()), N'Cần Thơ', 19990000, 1999000, 40000, 22029000, 'Processing', 'Card', 'Paid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 35 AS VARCHAR), 5), 6, 4, DATEADD(DAY, -2, GETDATE()), N'Hà Nội', 32990000, 3299000, 0, 36289000, 'Pending', 'Transfer', 'Unpaid'),
('ORD' + RIGHT('00000' + CAST(@OrderCounter + 36 AS VARCHAR), 5), 7, 3, DATEADD(DAY, -1, GETDATE()), N'Hải Phòng', 2200000, 220000, 30000, 2450000, 'Pending', 'COD', 'Unpaid');

GO


-- OrderDetails (matching orders above)
INSERT INTO OrderDetails (OrderId, ProductId, Quantity, UnitPrice, DiscountPercent, DiscountAmount, TaxPercent, TaxAmount, LineTotal) VALUES
-- Order 1
(1, 1, 1, 32990000, 0, 0, 10, 3299000, 36289000),
-- Order 2
(2, 4, 1, 45990000, 0, 0, 10, 4599000, 50589000),
-- Order 3
(3, 10, 1, 890000, 0, 0, 10, 89000, 979000),
-- Order 4
(4, 16, 1, 3500000, 0, 0, 10, 350000, 3850000),
(4, 17, 1, 2200000, 0, 0, 10, 220000, 2420000),
(4, 18, 1, 450000, 0, 0, 10, 45000, 495000),
-- Order 5
(5, 8, 1, 450000, 0, 0, 10, 45000, 495000),
-- Order 6
(6, 2, 1, 29990000, 0, 0, 10, 2999000, 32989000),
-- Order 7
(7, 5, 1, 35990000, 0, 0, 10, 3599000, 39589000),
-- Order 8
(8, 8, 2, 450000, 0, 0, 10, 90000, 990000),
(8, 9, 3, 199000, 0, 0, 10, 59700, 656700),
(8, 12, 1, 180000, 0, 0, 10, 18000, 198000),
-- Order 9
(9, 3, 1, 19990000, 0, 0, 10, 1999000, 21989000),
-- Order 10
(10, 7, 1, 28990000, 0, 0, 10, 2899000, 31889000),
-- Order 11
(11, 1, 1, 32990000, 0, 0, 10, 3299000, 36289000),
-- Order 12
(12, 16, 1, 3500000, 0, 0, 10, 350000, 3850000),
(12, 17, 1, 2200000, 0, 0, 10, 220000, 2420000),
-- Order 13
(13, 4, 1, 45990000, 0, 0, 10, 4599000, 50589000),
-- Order 14
(14, 11, 1, 1290000, 0, 0, 10, 129000, 1419000),
(14, 8, 2, 450000, 0, 0, 10, 90000, 990000),
-- Order 15
(15, 12, 1, 180000, 0, 0, 10, 18000, 198000),
-- Order 16
(16, 1, 1, 32990000, 0, 0, 10, 3299000, 36289000),
-- Order 17
(17, 2, 1, 29990000, 0, 0, 10, 2999000, 32989000),
-- Order 18
(18, 11, 1, 1290000, 0, 0, 10, 129000, 1419000),
-- Order 19
(19, 5, 1, 35990000, 0, 0, 10, 3599000, 39589000),
-- Order 20
(20, 8, 1, 450000, 0, 0, 10, 45000, 495000),
-- Order 21
(21, 4, 1, 45990000, 0, 0, 10, 4599000, 50589000),
-- Order 22
(22, 7, 1, 28990000, 0, 0, 10, 2899000, 31889000),
-- Order 23
(23, 1, 1, 32990000, 0, 0, 10, 3299000, 36289000),
-- Order 24
(24, 17, 1, 2200000, 0, 0, 10, 220000, 2420000),
-- Order 25
(25, 3, 1, 19990000, 0, 0, 10, 1999000, 21989000),
-- Order 26
(26, 5, 1, 35990000, 0, 0, 10, 3599000, 39589000),
-- Order 27
(27, 2, 1, 29990000, 0, 0, 10, 2999000, 32989000),
-- Order 28
(28, 8, 2, 450000, 0, 0, 10, 90000, 990000),
(28, 9, 2, 199000, 0, 0, 10, 39800, 437800),
(28, 12, 2, 180000, 0, 0, 10, 36000, 396000),
-- Order 29
(29, 4, 1, 45990000, 0, 0, 10, 4599000, 50589000),
-- Order 30
(30, 1, 1, 32990000, 0, 0, 10, 3299000, 36289000),
-- Order 31
(31, 7, 1, 28990000, 0, 0, 10, 2899000, 31889000),
-- Order 32
(32, 5, 1, 35990000, 0, 0, 10, 3599000, 39589000),
-- Order 33
(33, 10, 1, 890000, 0, 0, 10, 89000, 979000),
-- Order 34
(34, 4, 1, 45990000, 0, 0, 10, 4599000, 50589000),
-- Order 35
(35, 3, 1, 19990000, 0, 0, 10, 1999000, 21989000),
-- Order 36
(36, 1, 1, 32990000, 0, 0, 10, 3299000, 36289000),
-- Order 37
(37, 17, 1, 2200000, 0, 0, 10, 220000, 2420000);

-- OrderPromotions
INSERT INTO OrderPromotions (OrderId, PromotionId, DiscountAmount) VALUES
(2, 1, 500000),  -- Order 2 used SUMMER2024
(7, 1, 500000),  -- Order 7 used SUMMER2024
(13, 1, 500000), -- Order 13 used SUMMER2024
(21, 3, 1500000), -- Order 21 used VIP2024
(34, 3, 1500000); -- Order 34 used VIP2024

-- ProductReviews
INSERT INTO ProductReviews (ProductId, CustomerId, OrderId, Rating, ReviewText, ReviewDate, IsVerifiedPurchase) VALUES
(1, 1, 1, 5, N'Sản phẩm rất tốt, giao hàng nhanh', DATEADD(DAY, 7, '2023-01-05'), 1),
(4, 2, 2, 5, N'MacBook chất lượng cao, đáng tiền', DATEADD(DAY, 10, '2023-01-12'), 1),
(10, 3, 3, 4, N'Váy đẹp nhưng hơi nhỏ', DATEADD(DAY, 5, '2023-01-20'), 1),
(2, 5, 6, 5, N'Samsung S24 Ultra rất mượt', DATEADD(DAY, 8, '2023-04-05'), 1),
(3, 7, 9, 4, N'Xiaomi 14 Pro giá tốt, hiệu năng ổn', DATEADD(DAY, 6, '2023-05-15'), 1),
(1, 3, 11, 5, N'iPhone 15 Pro Max quá đỉnh!', DATEADD(DAY, 9, '2023-07-05'), 1),
(4, 4, 13, 5, N'MacBook Pro M3 xử lý mượt mà', DATEADD(DAY, 12, '2023-08-05'), 1),
(7, 2, 22, 4, N'iPad Pro màn hình đẹp', DATEADD(DAY, 7, '2024-02-10'), 1);

-- InventoryTransactions
INSERT INTO InventoryTransactions (ProductId, TransactionType, Quantity, ReferenceType, ReferenceId, TransactionDate, CreatedBy) VALUES
-- Initial stock
(1, 'In', 50, 'Adjustment', NULL, '2023-01-01', 5),
(2, 'In', 50, 'Adjustment', NULL, '2023-01-01', 5),
(3, 'In', 60, 'Adjustment', NULL, '2023-01-01', 5),
(4, 'In', 30, 'Adjustment', NULL, '2023-01-01', 5),
(5, 'In', 40, 'Adjustment', NULL, '2023-01-01', 5),
-- Order fulfillments
(1, 'Out', 1, 'Order', 1, '2023-01-05', 6),
(4, 'Out', 1, 'Order', 2, '2023-01-12', 6),
(10, 'Out', 1, 'Order', 3, '2023-01-20', 6),
(2, 'Out', 1, 'Order', 6, '2023-04-05', 6),
(3, 'Out', 1, 'Order', 9, '2023-05-15', 6),
-- Restocking
(1, 'In', 20, 'Adjustment', NULL, '2023-06-01', 5),
(2, 'In', 20, 'Adjustment', NULL, '2023-06-01', 5),
(3, 'In', 30, 'Adjustment', NULL, '2023-06-01', 5);

GO


-- =============================================
-- VIEWS for Complex Queries
-- =============================================

-- View: Customer Purchase Summary
CREATE VIEW vw_CustomerPurchaseSummary AS
SELECT 
    c.CustomerId,
    c.CustomerCode,
    c.FullName,
    c.City,
    c.CustomerType,
    COUNT(DISTINCT o.OrderId) AS TotalOrders,
    SUM(o.TotalAmount) AS TotalSpent,
    AVG(o.TotalAmount) AS AvgOrderValue,
    MAX(o.OrderDate) AS LastPurchaseDate,
    DATEDIFF(DAY, MAX(o.OrderDate), GETDATE()) AS DaysSinceLastPurchase
FROM Customers c
LEFT JOIN Orders o ON c.CustomerId = o.CustomerId AND o.Status = 'Delivered'
GROUP BY c.CustomerId, c.CustomerCode, c.FullName, c.City, c.CustomerType;
GO

-- View: Product Sales Summary
CREATE VIEW vw_ProductSalesSummary AS
SELECT 
    p.ProductId,
    p.ProductCode,
    p.ProductName,
    c.CategoryName,
    p.Brand,
    p.UnitPrice,
    p.StockQuantity,
    COUNT(DISTINCT od.OrderId) AS TotalOrders,
    SUM(od.Quantity) AS TotalQuantitySold,
    SUM(od.LineTotal) AS TotalRevenue,
    AVG(pr.Rating) AS AvgRating,
    COUNT(pr.ReviewId) AS ReviewCount
FROM Products p
LEFT JOIN Categories c ON p.CategoryId = c.CategoryId
LEFT JOIN OrderDetails od ON p.ProductId = od.ProductId
LEFT JOIN Orders o ON od.OrderId = o.OrderId AND o.Status = 'Delivered'
LEFT JOIN ProductReviews pr ON p.ProductId = pr.ProductId
GROUP BY p.ProductId, p.ProductCode, p.ProductName, c.CategoryName, p.Brand, p.UnitPrice, p.StockQuantity;
GO

-- View: Monthly Sales Report
CREATE VIEW vw_MonthlySalesReport AS
SELECT 
    YEAR(o.OrderDate) AS Year,
    MONTH(o.OrderDate) AS Month,
    DATEFROMPARTS(YEAR(o.OrderDate), MONTH(o.OrderDate), 1) AS MonthStart,
    COUNT(DISTINCT o.OrderId) AS TotalOrders,
    COUNT(DISTINCT o.CustomerId) AS UniqueCustomers,
    SUM(o.SubTotal) AS TotalSubTotal,
    SUM(o.DiscountAmount) AS TotalDiscount,
    SUM(o.TaxAmount) AS TotalTax,
    SUM(o.ShippingFee) AS TotalShipping,
    SUM(o.TotalAmount) AS TotalRevenue,
    AVG(o.TotalAmount) AS AvgOrderValue
FROM Orders o
WHERE o.Status IN ('Delivered', 'Shipped')
GROUP BY YEAR(o.OrderDate), MONTH(o.OrderDate);
GO

-- View: Employee Performance
CREATE VIEW vw_EmployeePerformance AS
SELECT 
    e.EmployeeId,
    e.EmployeeCode,
    e.FullName,
    e.Department,
    e.Position,
    COUNT(DISTINCT o.OrderId) AS TotalOrders,
    SUM(o.TotalAmount) AS TotalSales,
    AVG(o.TotalAmount) AS AvgOrderValue,
    COUNT(DISTINCT o.CustomerId) AS UniqueCustomers
FROM Employees e
LEFT JOIN Orders o ON e.EmployeeId = o.EmployeeId AND o.Status = 'Delivered'
GROUP BY e.EmployeeId, e.EmployeeCode, e.FullName, e.Department, e.Position;
GO

-- =============================================
-- STORED PROCEDURES for Complex Operations
-- =============================================

-- Procedure: Get Top Selling Products
CREATE PROCEDURE sp_GetTopSellingProducts
    @TopN INT = 10,
    @StartDate DATETIME = NULL,
    @EndDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT TOP (@TopN)
        p.ProductId,
        p.ProductCode,
        p.ProductName,
        c.CategoryName,
        p.Brand,
        SUM(od.Quantity) AS TotalQuantitySold,
        SUM(od.LineTotal) AS TotalRevenue,
        COUNT(DISTINCT od.OrderId) AS TotalOrders,
        AVG(od.UnitPrice) AS AvgSellingPrice
    FROM Products p
    INNER JOIN OrderDetails od ON p.ProductId = od.ProductId
    INNER JOIN Orders o ON od.OrderId = o.OrderId
    LEFT JOIN Categories c ON p.CategoryId = c.CategoryId
    WHERE o.Status = 'Delivered'
        AND (@StartDate IS NULL OR o.OrderDate >= @StartDate)
        AND (@EndDate IS NULL OR o.OrderDate <= @EndDate)
    GROUP BY p.ProductId, p.ProductCode, p.ProductName, c.CategoryName, p.Brand
    ORDER BY TotalRevenue DESC;
END;
GO

-- Procedure: Get Customer Lifetime Value
CREATE PROCEDURE sp_GetCustomerLifetimeValue
    @MinOrders INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    
    WITH CustomerMetrics AS (
        SELECT 
            c.CustomerId,
            c.CustomerCode,
            c.FullName,
            c.City,
            c.CustomerType,
            COUNT(DISTINCT o.OrderId) AS TotalOrders,
            SUM(o.TotalAmount) AS LifetimeValue,
            AVG(o.TotalAmount) AS AvgOrderValue,
            MIN(o.OrderDate) AS FirstOrderDate,
            MAX(o.OrderDate) AS LastOrderDate,
            DATEDIFF(DAY, MIN(o.OrderDate), MAX(o.OrderDate)) AS CustomerLifespanDays
        FROM Customers c
        INNER JOIN Orders o ON c.CustomerId = o.CustomerId
        WHERE o.Status = 'Delivered'
        GROUP BY c.CustomerId, c.CustomerCode, c.FullName, c.City, c.CustomerType
        HAVING COUNT(DISTINCT o.OrderId) >= @MinOrders
    )
    SELECT 
        *,
        CASE 
            WHEN CustomerLifespanDays > 0 
            THEN LifetimeValue / NULLIF(CustomerLifespanDays, 0) * 30
            ELSE 0 
        END AS MonthlyValue
    FROM CustomerMetrics
    ORDER BY LifetimeValue DESC;
END;
GO

-- Procedure: Sales Comparison by Period
CREATE PROCEDURE sp_CompareSalesByPeriod
    @Period1Start DATETIME,
    @Period1End DATETIME,
    @Period2Start DATETIME,
    @Period2End DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    
    WITH Period1Sales AS (
        SELECT 
            COUNT(DISTINCT OrderId) AS Orders,
            SUM(TotalAmount) AS Revenue,
            AVG(TotalAmount) AS AvgOrderValue
        FROM Orders
        WHERE OrderDate BETWEEN @Period1Start AND @Period1End
            AND Status = 'Delivered'
    ),
    Period2Sales AS (
        SELECT 
            COUNT(DISTINCT OrderId) AS Orders,
            SUM(TotalAmount) AS Revenue,
            AVG(TotalAmount) AS AvgOrderValue
        FROM Orders
        WHERE OrderDate BETWEEN @Period2Start AND @Period2End
            AND Status = 'Delivered'
    )
    SELECT 
        'Period 1' AS Period,
        @Period1Start AS StartDate,
        @Period1End AS EndDate,
        p1.Orders,
        p1.Revenue,
        p1.AvgOrderValue,
        CAST(NULL AS INT) AS OrdersChange,
        CAST(NULL AS DECIMAL(18,2)) AS RevenueChange,
        CAST(NULL AS DECIMAL(18,2)) AS AvgOrderValueChange
    FROM Period1Sales p1
    UNION ALL
    SELECT 
        'Period 2' AS Period,
        @Period2Start AS StartDate,
        @Period2End AS EndDate,
        p2.Orders,
        p2.Revenue,
        p2.AvgOrderValue,
        p2.Orders - p1.Orders AS OrdersChange,
        p2.Revenue - p1.Revenue AS RevenueChange,
        p2.AvgOrderValue - p1.AvgOrderValue AS AvgOrderValueChange
    FROM Period2Sales p2, Period1Sales p1;
END;
GO

-- =============================================
-- FUNCTIONS for Calculations
-- =============================================

-- Function: Calculate Customer Segment
CREATE FUNCTION fn_GetCustomerSegment(@CustomerId INT)
RETURNS NVARCHAR(20)
AS
BEGIN
    DECLARE @TotalSpent DECIMAL(18,2);
    DECLARE @OrderCount INT;
    DECLARE @Segment NVARCHAR(20);
    
    SELECT 
        @TotalSpent = SUM(TotalAmount),
        @OrderCount = COUNT(*)
    FROM Orders
    WHERE CustomerId = @CustomerId AND Status = 'Delivered';
    
    SET @Segment = CASE
        WHEN @TotalSpent >= 100000000 THEN 'Platinum'
        WHEN @TotalSpent >= 50000000 THEN 'Gold'
        WHEN @TotalSpent >= 20000000 THEN 'Silver'
        WHEN @OrderCount >= 5 THEN 'Bronze'
        ELSE 'New'
    END;
    
    RETURN @Segment;
END;
GO

-- =============================================
-- Update Customer TotalSpent
-- =============================================
UPDATE c
SET c.TotalSpent = ISNULL(o.TotalSpent, 0),
    c.LastPurchaseDate = o.LastPurchaseDate
FROM Customers c
LEFT JOIN (
    SELECT 
        CustomerId,
        SUM(TotalAmount) AS TotalSpent,
        MAX(OrderDate) AS LastPurchaseDate
    FROM Orders
    WHERE Status = 'Delivered'
    GROUP BY CustomerId
) o ON c.CustomerId = o.CustomerId;
GO

PRINT '✓ Database setup completed successfully!';
PRINT '';
PRINT '==============================================';
PRINT 'Connection Information:';
PRINT '==============================================';
PRINT 'Server: localhost (or .)';
PRINT 'Database: TextToSqlTest';
PRINT 'User: sa';
PRINT 'Password: 123';
PRINT '';
PRINT 'Connection String:';
PRINT 'Server=localhost;Database=TextToSqlTest;User Id=sa;Password=123;TrustServerCertificate=True;';
PRINT '';
PRINT '==============================================';
PRINT 'Database Statistics:';
PRINT '==============================================';
SELECT 'Customers' AS TableName, COUNT(*) AS RecordCount FROM Customers
UNION ALL SELECT 'Categories', COUNT(*) FROM Categories
UNION ALL SELECT 'Products', COUNT(*) FROM Products
UNION ALL SELECT 'Suppliers', COUNT(*) FROM Suppliers
UNION ALL SELECT 'Employees', COUNT(*) FROM Employees
UNION ALL SELECT 'Orders', COUNT(*) FROM Orders
UNION ALL SELECT 'OrderDetails', COUNT(*) FROM OrderDetails
UNION ALL SELECT 'Promotions', COUNT(*) FROM Promotions
UNION ALL SELECT 'ProductReviews', COUNT(*) FROM ProductReviews
UNION ALL SELECT 'InventoryTransactions', COUNT(*) FROM InventoryTransactions;
GO
