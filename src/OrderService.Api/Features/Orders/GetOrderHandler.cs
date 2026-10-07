using Microsoft.EntityFrameworkCore;
using OrderService.Api.Infrastructure;

namespace OrderService.Api.Features.Orders;

/// <summary>
/// Order status tracking: the read side of the flow in Part 1. A projection, so no
/// entities are materialised or tracked.
/// </summary>
public sealed class GetOrderHandler(OrderDbContext db)
{
    public Task<GetOrderResponse?> GetOrderAsync(long orderId, CancellationToken cancellationToken) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new GetOrderResponse(
                o.OrderId,
                o.CustomerId,
                o.OrderStatus,
                o.TotalAmount,
                o.CreatedAt,
                o.UpdatedAt,
                o.Items.Select(i => new GetOrderItemResponse(i.ProductId, i.Quantity, i.UnitPrice)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
}
