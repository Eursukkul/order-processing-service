namespace OrderService.Api.Features.Orders;

public enum CreateOrderOutcome
{
    Created = 0,
    CustomerNotFound,
    ProductNotFound,
    ProductInactive,
    InsufficientStock
}

/// <summary>
/// Outcome of a create-order attempt. Expected business failures are returned as
/// values, not thrown: they are part of the endpoint's contract, and exceptions for
/// control flow would hide them from the compiler and cost a stack walk per rejection.
/// Unexpected failures (broken connection, deadlock) are still exceptions.
/// </summary>
public sealed record CreateOrderResult
{
    private CreateOrderResult(CreateOrderOutcome outcome, CreateOrderResponse? response, string? detail)
    {
        Outcome = outcome;
        Response = response;
        Detail = detail;
    }

    public CreateOrderOutcome Outcome { get; }

    /// <summary>Populated only when <see cref="Outcome"/> is <see cref="CreateOrderOutcome.Created"/>.</summary>
    public CreateOrderResponse? Response { get; }

    /// <summary>Human-readable reason for a rejection, safe to return to the caller.</summary>
    public string? Detail { get; }

    public bool IsSuccess => Outcome == CreateOrderOutcome.Created;

    public static CreateOrderResult Created(CreateOrderResponse response) =>
        new(CreateOrderOutcome.Created, response, null);

    public static CreateOrderResult CustomerNotFound(int customerId) =>
        new(CreateOrderOutcome.CustomerNotFound, null, $"Customer {customerId} was not found.");

    public static CreateOrderResult ProductNotFound(IEnumerable<int> productIds) =>
        new(CreateOrderOutcome.ProductNotFound, null,
            $"Product(s) not found: {string.Join(", ", productIds)}.");

    public static CreateOrderResult ProductInactive(IEnumerable<int> productIds) =>
        new(CreateOrderOutcome.ProductInactive, null,
            $"Product(s) are not available for sale: {string.Join(", ", productIds)}.");

    public static CreateOrderResult InsufficientStock(int productId, int requested, int? available = null) =>
        new(CreateOrderOutcome.InsufficientStock, null,
            available is null
                ? $"Insufficient stock for product {productId} (requested {requested})."
                : $"Insufficient stock for product {productId} (requested {requested}, available {available}).");
}
