/* Schema + seed data so 2.1-create-order.sql can actually be run. */

IF OBJECT_ID('dbo.OrderItems') IS NOT NULL DROP TABLE dbo.OrderItems;
IF OBJECT_ID('dbo.Orders')     IS NOT NULL DROP TABLE dbo.Orders;
IF OBJECT_ID('dbo.Products')   IS NOT NULL DROP TABLE dbo.Products;
IF OBJECT_ID('dbo.Customers')  IS NOT NULL DROP TABLE dbo.Customers;
GO

CREATE TABLE dbo.Customers
(
    CustomerId   INT            IDENTITY(1,1) CONSTRAINT PK_Customers PRIMARY KEY,
    CustomerName NVARCHAR(200)  NOT NULL,
    Email        NVARCHAR(320)  NOT NULL CONSTRAINT UQ_Customers_Email UNIQUE,
    CreatedAt    DATETIME2      NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Products
(
    ProductId     INT            IDENTITY(1,1) CONSTRAINT PK_Products PRIMARY KEY,
    ProductName   NVARCHAR(200)  NOT NULL,
    Price         DECIMAL(18,2)  NOT NULL CONSTRAINT CK_Products_Price CHECK (Price >= 0),
    StockQuantity INT            NOT NULL CONSTRAINT CK_Products_Stock CHECK (StockQuantity >= 0),
    IsActive      BIT            NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT 1
);

CREATE TABLE dbo.Orders
(
    OrderId     BIGINT        IDENTITY(1,1) CONSTRAINT PK_Orders PRIMARY KEY,
    CustomerId  INT           NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers(CustomerId),
    OrderStatus VARCHAR(30)   NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    CreatedAt   DATETIME2     NOT NULL,
    UpdatedAt   DATETIME2     NOT NULL
);
CREATE INDEX IX_Orders_CustomerId_CreatedAt ON dbo.Orders(CustomerId, CreatedAt DESC);

CREATE TABLE dbo.OrderItems
(
    OrderItemId BIGINT        IDENTITY(1,1) CONSTRAINT PK_OrderItems PRIMARY KEY,
    OrderId     BIGINT        NOT NULL CONSTRAINT FK_OrderItems_Orders   REFERENCES dbo.Orders(OrderId),
    ProductId   INT           NOT NULL CONSTRAINT FK_OrderItems_Products REFERENCES dbo.Products(ProductId),
    Quantity    INT           NOT NULL CONSTRAINT CK_OrderItems_Qty CHECK (Quantity > 0),
    UnitPrice   DECIMAL(18,2) NOT NULL
);
CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems(OrderId);
GO

/* Idempotency ledger referenced in Part 1.5 */
IF OBJECT_ID('dbo.ProcessedMessages') IS NULL
CREATE TABLE dbo.ProcessedMessages
(
    MessageId   UNIQUEIDENTIFIER NOT NULL,
    Consumer    VARCHAR(100)     NOT NULL,
    ProcessedAt DATETIME2        NOT NULL CONSTRAINT DF_PM_ProcessedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ProcessedMessages PRIMARY KEY (MessageId, Consumer)
);
GO

/* Seed: force CustomerId = 101 and ProductIds 1001 / 1002 to match the exercise. */
SET IDENTITY_INSERT dbo.Customers ON;
INSERT INTO dbo.Customers (CustomerId, CustomerName, Email, CreatedAt)
VALUES (101, N'Somchai Jaidee', N'somchai@example.com', SYSUTCDATETIME());
SET IDENTITY_INSERT dbo.Customers OFF;

SET IDENTITY_INSERT dbo.Products ON;
INSERT INTO dbo.Products (ProductId, ProductName, Price, StockQuantity, IsActive)
VALUES (1001, N'Mechanical Keyboard', 2500.00, 10, 1),
       (1002, N'USB-C Hub',            990.00,  5, 1),
       (1003, N'Discontinued Mouse',   450.00,  3, 0);
SET IDENTITY_INSERT dbo.Products OFF;
GO
