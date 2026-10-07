namespace OrderService.Api.Domain;

public class OrderItem
{
    public long OrderItemId { get; set; }
    public long OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }

    /// <summary>Price captured at order time, so later price changes never rewrite history.</summary>
    public decimal UnitPrice { get; set; }
}
