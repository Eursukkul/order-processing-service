/* =============================================================================
   Part 2.1 - Create an Order (SQL Server)

   Customer : 101
   Items    : Product 1001 x 2, Product 1002 x 1

   - prices are read from Products (never hard-coded)
   - the total is derived from those prices
   - Orders + OrderItems are inserted in one atomic transaction
   - initial status is 'Pending'
   - the new OrderId is returned
   - any failure rolls the whole thing back
   ========================================================================== */

USE OrderDb;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;          -- any run-time error aborts the transaction outright

DECLARE @CustomerId INT = 101;
DECLARE @NewOrderId BIGINT = NULL;
DECLARE @Now        DATETIME2(7) = SYSUTCDATETIME();

/* The requested basket. A table variable keeps the whole script set-based, so
   it generalises to N items without changing the body. */
DECLARE @Items TABLE
(
    ProductId INT NOT NULL PRIMARY KEY,
    Quantity  INT NOT NULL CHECK (Quantity > 0)
);

INSERT INTO @Items (ProductId, Quantity)
VALUES (1001, 2),
       (1002, 1);

/* Prices captured at reservation time - this is what the order is priced on. */
DECLARE @Reserved TABLE
(
    ProductId INT           NOT NULL PRIMARY KEY,
    UnitPrice DECIMAL(18,2) NOT NULL
);

BEGIN TRY
    BEGIN TRANSACTION;

    /* ---------------------------------------------------------------------
       1. Customer must exist. Checked first so we never take inventory locks
          for a request that cannot succeed.
       --------------------------------------------------------------------- */
    IF NOT EXISTS (SELECT 1 FROM Customers WHERE CustomerId = @CustomerId)
        THROW 50002, N'Order rejected: customer does not exist.', 1;

    /* ---------------------------------------------------------------------
       2. Reserve stock and capture prices in one atomic statement.

          The availability guard (StockQuantity >= Quantity) lives inside the
          UPDATE, so the check and the decrement cannot be interleaved by a
          concurrent order. SQL Server holds an exclusive key lock per row for
          the duration of the statement, so two buyers of the last unit
          serialise: the second one matches no rows. See Part 1.4.

          Products is keyed on ProductId, so the join drives a clustered-index
          seek and rows are touched in key order. For a hard guarantee of lock
          ordering across statements (multi-statement checkout flows), iterate
          the basket ORDER BY ProductId instead.
       --------------------------------------------------------------------- */
    UPDATE p
       SET p.StockQuantity = p.StockQuantity - i.Quantity
    OUTPUT inserted.ProductId, deleted.Price
      INTO @Reserved (ProductId, UnitPrice)
      FROM Products AS p WITH (ROWLOCK, UPDLOCK)
      JOIN @Items   AS i ON i.ProductId = p.ProductId
     WHERE p.IsActive = 1
       AND p.StockQuantity >= i.Quantity;

    /* Anything missing, inactive, or short on stock simply did not update, so
       fewer rows came back than were asked for. */
    IF (SELECT COUNT(*) FROM @Reserved) <> (SELECT COUNT(*) FROM @Items)
    BEGIN
        DECLARE @Unavailable NVARCHAR(400) =
        (
            SELECT STRING_AGG(CONVERT(NVARCHAR(20), i.ProductId), N', ')
              FROM @Items AS i
             WHERE NOT EXISTS (SELECT 1 FROM @Reserved AS r WHERE r.ProductId = i.ProductId)
        );

        /* RAISERROR rather than THROW: it accepts a runtime message, so the
           caller learns which products failed. Severity 16 + XACT_ABORT ON
           still unwinds into the CATCH block below. */
        RAISERROR(N'Order rejected: product missing, inactive, or insufficient stock (ProductId: %s).',
                  16, 1, @Unavailable);
    END

    /* ---------------------------------------------------------------------
       3. Order header, total derived from the reserved prices.
       --------------------------------------------------------------------- */
    DECLARE @TotalAmount DECIMAL(18,2) =
    (
        SELECT SUM(i.Quantity * r.UnitPrice)
          FROM @Items    AS i
          JOIN @Reserved AS r ON r.ProductId = i.ProductId
    );

    INSERT INTO Orders (CustomerId, OrderStatus, TotalAmount, CreatedAt, UpdatedAt)
    VALUES (@CustomerId, 'Pending', @TotalAmount, @Now, @Now);

    SET @NewOrderId = CAST(SCOPE_IDENTITY() AS BIGINT);

    /* ---------------------------------------------------------------------
       4. Line items, priced from the reservation.
       --------------------------------------------------------------------- */
    INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice)
    SELECT @NewOrderId, i.ProductId, i.Quantity, r.UnitPrice
      FROM @Items    AS i
      JOIN @Reserved AS r ON r.ProductId = i.ProductId;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;   -- stock decrement and both inserts are undone together

    SET @NewOrderId = NULL;
    THROW;                      -- preserve the original error number/message/severity
END CATCH

/* 5. Return the newly created OrderId. */
SELECT @NewOrderId AS OrderId;
