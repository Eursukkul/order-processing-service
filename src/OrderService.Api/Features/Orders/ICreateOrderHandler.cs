namespace OrderService.Api.Features.Orders;

public interface ICreateOrderHandler
{
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);
}
