using OrderService.Api.Domain;
using OrderService.Api.Features.Orders;

namespace OrderService.Tests;

public sealed class GetOrderHandlerTests : IDisposable
{
    private static readonly DateTime CreatedAt = new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);

    private readonly OrderDbContextFixture _fixture = new OrderDbContextFixture().Seed();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetOrderAsync_WhenOrderExists_ReturnsStatusAndItems()
    {
        long orderId;
        await using (var setup = _fixture.CreateContext())
        {
            var order = new Order
            {
                CustomerId = 101,
                OrderStatus = OrderStatus.Pending,
                TotalAmount = 5990.00m,
                CreatedAt = CreatedAt,
                UpdatedAt = CreatedAt,
                Items =
                [
                    new OrderItem { ProductId = 1001, Quantity = 2, UnitPrice = 2500.00m },
                    new OrderItem { ProductId = 1002, Quantity = 1, UnitPrice = 990.00m }
                ]
            };
            setup.Orders.Add(order);
            await setup.SaveChangesAsync();
            orderId = order.OrderId;
        }

        await using var context = _fixture.CreateContext();
        var response = await new GetOrderHandler(context).GetOrderAsync(orderId, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(orderId, response.OrderId);
        Assert.Equal(101, response.CustomerId);
        Assert.Equal(OrderStatus.Pending, response.OrderStatus);
        Assert.Equal(5990.00m, response.TotalAmount);

        // Read back as UTC so JSON carries the "Z" suffix, not an ambiguous local time.
        Assert.Equal(CreatedAt, response.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, response.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, response.UpdatedAt.Kind);

        Assert.Collection(response.Items.OrderBy(i => i.ProductId),
            first =>
            {
                Assert.Equal(1001, first.ProductId);
                Assert.Equal(2, first.Quantity);
                Assert.Equal(2500.00m, first.UnitPrice);
            },
            second =>
            {
                Assert.Equal(1002, second.ProductId);
                Assert.Equal(1, second.Quantity);
                Assert.Equal(990.00m, second.UnitPrice);
            });
    }

    [Fact]
    public async Task GetOrderAsync_WhenOrderDoesNotExist_ReturnsNull()
    {
        var response = await new GetOrderHandler(_fixture.Context).GetOrderAsync(999_999, CancellationToken.None);

        Assert.Null(response);
    }
}
