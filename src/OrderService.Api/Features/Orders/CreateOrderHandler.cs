using Microsoft.EntityFrameworkCore;
using OrderService.Api.Domain;
using OrderService.Api.Infrastructure;

namespace OrderService.Api.Features.Orders;

/// <summary>
/// Order creation: the only synchronous write in the flow designed in Part 1.
/// Everything after the commit (payment, shipment, notification) is driven off
/// RabbitMQ and is deliberately out of scope here.
/// </summary>
public sealed class CreateOrderHandler(
    OrderDbContext db,
    TimeProvider timeProvider,
    ILogger<CreateOrderHandler> logger) : ICreateOrderHandler
{
    public async Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedQuantities = request.Items.ToDictionary(i => i.ProductId, i => i.Quantity);
        var productIds = requestedQuantities.Keys.ToArray();

        // --- 1. Customer must exist ------------------------------------------------
        var customerExists = await db.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerId == request.CustomerId, cancellationToken);

        if (!customerExists)
        {
            logger.CustomerNotFound(request.CustomerId);
            return CreateOrderResult.CustomerNotFound(request.CustomerId);
        }

        // --- 2. Products must exist, be active, and look available -----------------
        // This read produces a precise error message and the prices used for the
        // total. It is NOT the availability guarantee: between this read and the
        // reservation below another order can take the stock, which is exactly why
        // step 3 re-checks atomically.
        var products = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, cancellationToken);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            logger.ProductsNotFound(string.Join(", ", missing));
            return CreateOrderResult.ProductNotFound(missing);
        }

        var inactive = products.Values.Where(p => !p.IsActive).Select(p => p.ProductId).ToArray();
        if (inactive.Length > 0)
        {
            logger.ProductsInactive(string.Join(", ", inactive));
            return CreateOrderResult.ProductInactive(inactive);
        }

        foreach (var (productId, quantity) in requestedQuantities)
        {
            var product = products[productId];
            if (product.StockQuantity < quantity)
            {
                logger.InsufficientStock(productId, quantity, product.StockQuantity);
                return CreateOrderResult.InsufficientStock(productId, quantity, product.StockQuantity);
            }
        }

        // --- 3. Reserve stock + persist the order as one unit ----------------------
        // The connection is configured with EnableRetryOnFailure, so a user-initiated
        // transaction must run inside an execution strategy: on a transient fault the
        // whole block is retried, not just the failed statement. Everything mutable is
        // built inside the delegate so a retry starts from a clean change tracker.
        var strategy = db.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();

            var now = timeProvider.GetUtcNow().UtcDateTime;

            var order = new Order
            {
                CustomerId = request.CustomerId,
                OrderStatus = Domain.OrderStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Items = [.. request.Items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = products[i.ProductId].Price   // price from the database, never the client
                })]
            };

            order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Ascending ProductId so concurrent multi-item orders always acquire row
            // locks in the same sequence and cannot deadlock against each other.
            foreach (var item in request.Items.OrderBy(i => i.ProductId))
            {
                var rowsAffected = await db.Products
                    .Where(p => p.ProductId == item.ProductId
                                && p.IsActive
                                && p.StockQuantity >= item.Quantity)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(p => p.StockQuantity, p => p.StockQuantity - item.Quantity),
                        ct);

                // The availability guard is inside the UPDATE, so check-and-decrement is a
                // single atomic statement. Zero rows means someone else took the stock
                // first - the order is rejected rather than oversold (see Part 1.4).
                if (rowsAffected == 0)
                {
                    await transaction.RollbackAsync(ct);
                    logger.StockReservationLost(item.ProductId, item.Quantity);
                    return CreateOrderResult.InsufficientStock(item.ProductId, item.Quantity);
                }
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            // In the full design an OutboxMessages row for OrderCreated is inserted here,
            // inside this same transaction, and published by a background dispatcher.

            await transaction.CommitAsync(ct);

            return CreateOrderResult.Created(
                new CreateOrderResponse(order.OrderId, order.OrderStatus, order.TotalAmount));
        }, cancellationToken);

        if (result.IsSuccess)
        {
            logger.OrderCreated(result.Response!.OrderId, request.CustomerId, result.Response.TotalAmount);
        }

        return result;
    }
}
