using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace OrderService.Api.Features.Orders;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders")
            .WithTags("Orders");

        orders.MapPost("/", CreateOrderAsync)
            .WithName("CreateOrder")
            .WithSummary("Places a new order and reserves inventory.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// Translates the handler's outcome into HTTP. The status codes are the contract:
    /// 404 for something that does not exist, 409 for a state conflict the client can
    /// retry against different data, 400 for a malformed request.
    /// </summary>
    private static async Task<Results<Created<CreateOrderResponse>, ValidationProblem, ProblemHttpResult>>
        CreateOrderAsync(
            CreateOrderRequest request,
            ICreateOrderHandler handler,
            CancellationToken cancellationToken)
    {
        if (!CreateOrderRequestValidator.TryValidate(request, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await handler.CreateOrderAsync(request, cancellationToken);

        return result.Outcome switch
        {
            CreateOrderOutcome.Created => TypedResults.Created(
                $"/api/orders/{result.Response!.OrderId}", result.Response),

            CreateOrderOutcome.CustomerNotFound or CreateOrderOutcome.ProductNotFound =>
                Problem(StatusCodes.Status404NotFound, "Resource not found", result.Detail),

            CreateOrderOutcome.ProductInactive =>
                Problem(StatusCodes.Status409Conflict, "Product unavailable", result.Detail),

            CreateOrderOutcome.InsufficientStock =>
                Problem(StatusCodes.Status409Conflict, "Insufficient stock", result.Detail),

            _ => Problem(StatusCodes.Status500InternalServerError, "Unexpected outcome", null)
        };
    }

    private static ProblemHttpResult Problem(int statusCode, string title, string? detail) =>
        TypedResults.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        });
}
