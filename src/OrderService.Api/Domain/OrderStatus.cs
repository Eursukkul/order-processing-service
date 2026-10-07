namespace OrderService.Api.Domain;

/// <summary>
/// Order lifecycle. Persisted as a string in <c>Orders.OrderStatus VARCHAR(30)</c> so the
/// column stays readable in the database and is not coupled to enum ordinals.
/// </summary>
public static class OrderStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string PaymentFailed = "PaymentFailed";
    public const string ReadyForShipment = "ReadyForShipment";
    public const string Shipped = "Shipped";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}
