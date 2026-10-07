namespace OrderService.Api.Features.Orders;

/// <summary>
/// Source-generated logging. Compile-time message templates cost nothing when the
/// level is disabled and keep the structured field names stable for log queries.
/// </summary>
internal static partial class CreateOrderLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Order {OrderId} created for customer {CustomerId} with total {TotalAmount}.")]
    public static partial void OrderCreated(this ILogger logger, long orderId, int customerId, decimal totalAmount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Order rejected: customer {CustomerId} not found.")]
    public static partial void CustomerNotFound(this ILogger logger, int customerId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Order rejected: products not found ({ProductIds}).")]
    public static partial void ProductsNotFound(this ILogger logger, string productIds);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Order rejected: products inactive ({ProductIds}).")]
    public static partial void ProductsInactive(this ILogger logger, string productIds);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Order rejected: product {ProductId} requested {Requested} but only {Available} in stock.")]
    public static partial void InsufficientStock(this ILogger logger, int productId, int requested, int available);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Order rejected: lost the race for product {ProductId} ({Requested} units); stock was taken by a concurrent order.")]
    public static partial void StockReservationLost(this ILogger logger, int productId, int requested);
}
