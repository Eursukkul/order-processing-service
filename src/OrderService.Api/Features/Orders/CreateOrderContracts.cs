namespace OrderService.Api.Features.Orders;

/// <summary>Request body for <c>POST /api/orders</c>.</summary>
/// <param name="CustomerId">Existing customer placing the order.</param>
/// <param name="Items">At least one line item. Prices are never accepted from the client.</param>
public sealed record CreateOrderRequest(int CustomerId, IReadOnlyList<CreateOrderItem> Items);

public sealed record CreateOrderItem(int ProductId, int Quantity);

public sealed record CreateOrderResponse(long OrderId, string OrderStatus, decimal TotalAmount);
