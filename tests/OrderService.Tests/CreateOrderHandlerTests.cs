using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OrderService.Api.Domain;
using OrderService.Api.Features.Orders;
using OrderService.Api.Infrastructure;

namespace OrderService.Tests;

public sealed class CreateOrderHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    private readonly OrderDbContextFixture _fixture = new OrderDbContextFixture().Seed();
    private readonly FakeTimeProvider _time = new(Now);

    private CreateOrderHandler CreateSut(OrderDbContext? context = null) =>
        new(context ?? _fixture.Context, _time, NullLogger<CreateOrderHandler>.Instance);

    public void Dispose() => _fixture.Dispose();

    // ---------------------------------------------------------------- happy path

    [Fact]
    public async Task CreateOrderAsync_WithAvailableProducts_CreatesPendingOrderPricedFromTheCatalogue()
    {
        // Arrange - the Part 2.1 basket: 2 x 2500.00 + 1 x 990.00 = 5990.00
        var request = new CreateOrderRequest(101,
        [
            new CreateOrderItem(1001, 2),
            new CreateOrderItem(1002, 1)
        ]);

        // Act
        var result = await CreateSut().CreateOrderAsync(request, CancellationToken.None);

        // Assert - the response contract
        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        Assert.NotNull(result.Response);
        Assert.True(result.Response.OrderId > 0);
        Assert.Equal(OrderStatus.Pending, result.Response.OrderStatus);
        Assert.Equal(5990.00m, result.Response.TotalAmount);

        // Assert - what was actually persisted, read back through a fresh context
        await using var verify = _fixture.CreateContext();

        var order = await verify.Orders
            .Include(o => o.Items)
            .SingleAsync(o => o.OrderId == result.Response.OrderId);

        Assert.Equal(101, order.CustomerId);
        Assert.Equal(OrderStatus.Pending, order.OrderStatus);
        Assert.Equal(5990.00m, order.TotalAmount);
        Assert.Equal(Now.UtcDateTime, order.CreatedAt);
        Assert.Equal(Now.UtcDateTime, order.UpdatedAt);

        Assert.Collection(order.Items.OrderBy(i => i.ProductId),
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

        // Assert - inventory was reserved by exactly the ordered quantity
        Assert.Equal(8, await verify.Products.Where(p => p.ProductId == 1001).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(4, await verify.Products.Where(p => p.ProductId == 1002).Select(p => p.StockQuantity).SingleAsync());
    }

    // ------------------------------------------------------- product unavailable

    [Fact]
    public async Task CreateOrderAsync_WhenRequestedQuantityExceedsStock_RejectsAndLeavesStockUntouched()
    {
        var request = new CreateOrderRequest(101, [new CreateOrderItem(1002, 6)]); // only 5 in stock

        var result = await CreateSut().CreateOrderAsync(request, CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.InsufficientStock, result.Outcome);
        Assert.Null(result.Response);
        Assert.Contains("1002", result.Detail);

        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.Orders.AnyAsync());
        Assert.Equal(5, await verify.Products.Where(p => p.ProductId == 1002).Select(p => p.StockQuantity).SingleAsync());
    }

    [Fact]
    public async Task CreateOrderAsync_WhenProductIsInactive_RejectsTheOrder()
    {
        var request = new CreateOrderRequest(101, [new CreateOrderItem(1003, 1)]); // IsActive = false

        var result = await CreateSut().CreateOrderAsync(request, CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.ProductInactive, result.Outcome);
        Assert.Contains("1003", result.Detail);

        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.Orders.AnyAsync());
    }

    [Fact]
    public async Task CreateOrderAsync_WhenProductDoesNotExist_RejectsTheOrder()
    {
        var request = new CreateOrderRequest(101, [new CreateOrderItem(999_999, 1)]);

        var result = await CreateSut().CreateOrderAsync(request, CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.ProductNotFound, result.Outcome);
        Assert.Contains("999999", result.Detail);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenCustomerDoesNotExist_RejectsBeforeTouchingInventory()
    {
        var request = new CreateOrderRequest(404, [new CreateOrderItem(1001, 1)]);

        var result = await CreateSut().CreateOrderAsync(request, CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.CustomerNotFound, result.Outcome);

        await using var verify = _fixture.CreateContext();
        Assert.Equal(10, await verify.Products.Where(p => p.ProductId == 1001).Select(p => p.StockQuantity).SingleAsync());
    }

    // ------------------------------------------------------------- atomicity

    [Fact]
    public async Task CreateOrderAsync_WhenOneItemOfManyIsUnavailable_RollsBackTheWholeOrder()
    {
        // 1001 is available, 1004 is sold out. The first reservation must be undone.
        var request = new CreateOrderRequest(101,
        [
            new CreateOrderItem(1001, 1),
            new CreateOrderItem(1004, 1)
        ]);

        var result = await CreateSut().CreateOrderAsync(request, CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.InsufficientStock, result.Outcome);

        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.Orders.AnyAsync());
        Assert.False(await verify.OrderItems.AnyAsync());
        Assert.Equal(10, await verify.Products.Where(p => p.ProductId == 1001).Select(p => p.StockQuantity).SingleAsync());
    }

    // ------------------------------------------------------------- concurrency

    [Fact]
    public async Task CreateOrderAsync_WhenTwoOrdersRaceForTheLastUnit_OnlyOneSucceeds()
    {
        // Drive stock for 1001 down to a single unit.
        await using (var setup = _fixture.CreateContext())
        {
            await setup.Products
                .Where(p => p.ProductId == 1001)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, 1));
        }

        var request = new CreateOrderRequest(101, [new CreateOrderItem(1001, 1)]);

        // Separate DbContext per caller, mirroring two concurrent HTTP requests.
        // SQLite serialises writers, so this asserts the *guard*, not raw parallelism:
        // the loser's conditional UPDATE matches zero rows and the order is rejected.
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();

        var first = await CreateSut(firstContext).CreateOrderAsync(request, CancellationToken.None);
        var second = await CreateSut(secondContext).CreateOrderAsync(request, CancellationToken.None);

        Assert.Equal(CreateOrderOutcome.Created, first.Outcome);
        Assert.Equal(CreateOrderOutcome.InsufficientStock, second.Outcome);

        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.Orders.CountAsync());
        Assert.Equal(0, await verify.Products.Where(p => p.ProductId == 1001).Select(p => p.StockQuantity).SingleAsync());
    }
}
