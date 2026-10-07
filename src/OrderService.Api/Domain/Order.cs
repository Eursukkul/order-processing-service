namespace OrderService.Api.Domain;

public class Order
{
    public long OrderId { get; set; }
    public int CustomerId { get; set; }
    public string OrderStatus { get; set; } = Domain.OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}
