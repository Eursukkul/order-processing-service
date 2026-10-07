using OrderService.Api.Features.Orders;

namespace OrderService.Tests;

public sealed class CreateOrderRequestValidatorTests
{
    [Fact]
    public void TryValidate_WithAValidRequest_Passes()
    {
        var request = new CreateOrderRequest(101, [new CreateOrderItem(1001, 2)]);

        Assert.True(CreateOrderRequestValidator.TryValidate(request, out var errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void TryValidate_WithNoItems_Fails()
    {
        var request = new CreateOrderRequest(101, []);

        Assert.False(CreateOrderRequestValidator.TryValidate(request, out var errors));
        Assert.True(errors.ContainsKey(nameof(CreateOrderRequest.Items)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryValidate_WithNonPositiveQuantity_Fails(int quantity)
    {
        var request = new CreateOrderRequest(101, [new CreateOrderItem(1001, quantity)]);

        Assert.False(CreateOrderRequestValidator.TryValidate(request, out var errors));
        Assert.True(errors.ContainsKey("Items[0]"));
    }

    [Fact]
    public void TryValidate_WithDuplicateProductIds_Fails()
    {
        // Two lines for one product would make the atomic per-product reservation ambiguous.
        var request = new CreateOrderRequest(101,
        [
            new CreateOrderItem(1001, 1),
            new CreateOrderItem(1001, 2)
        ]);

        Assert.False(CreateOrderRequestValidator.TryValidate(request, out var errors));
        Assert.Contains("Duplicate", errors[nameof(CreateOrderRequest.Items)][0]);
    }

    [Fact]
    public void TryValidate_WithInvalidCustomerId_Fails()
    {
        var request = new CreateOrderRequest(0, [new CreateOrderItem(1001, 1)]);

        Assert.False(CreateOrderRequestValidator.TryValidate(request, out var errors));
        Assert.True(errors.ContainsKey(nameof(CreateOrderRequest.CustomerId)));
    }
}
