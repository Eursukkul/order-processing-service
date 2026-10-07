namespace OrderService.Api.Features.Orders;

/// <summary>
/// Shape validation only - everything checkable without touching the database.
/// Kept hand-written rather than pulling in a validation library: the rule set is
/// small, and the errors come out in the exact shape <c>ValidationProblem</c> wants.
/// </summary>
public static class CreateOrderRequestValidator
{
    public const int MaxItemsPerOrder = 100;
    public const int MaxQuantityPerItem = 1_000;

    public static bool TryValidate(
        CreateOrderRequest? request,
        out Dictionary<string, string[]> errors)
    {
        errors = [];

        if (request is null)
        {
            errors[""] = ["A request body is required."];
            return false;
        }

        if (request.CustomerId <= 0)
        {
            errors[nameof(request.CustomerId)] = ["CustomerId must be greater than zero."];
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            errors[nameof(request.Items)] = ["An order must contain at least one item."];
            return false;
        }

        if (request.Items.Count > MaxItemsPerOrder)
        {
            errors[nameof(request.Items)] = [$"An order cannot contain more than {MaxItemsPerOrder} items."];
        }

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];

            if (item is null)
            {
                errors[$"Items[{index}]"] = ["Item is required."];
                continue;
            }

            var itemErrors = new List<string>();

            if (item.ProductId <= 0)
            {
                itemErrors.Add("ProductId must be greater than zero.");
            }

            if (item.Quantity <= 0)
            {
                itemErrors.Add("Quantity must be greater than zero.");
            }
            else if (item.Quantity > MaxQuantityPerItem)
            {
                itemErrors.Add($"Quantity cannot exceed {MaxQuantityPerItem}.");
            }

            if (itemErrors.Count > 0)
            {
                errors[$"Items[{index}]"] = [.. itemErrors];
            }
        }

        // Duplicates would make the per-product atomic stock decrement ambiguous
        // (two partial reservations for one row). Reject instead of silently merging,
        // so the client's intent is never guessed at.
        var duplicateIds = request.Items
            .Where(i => i is not null)
            .GroupBy(i => i.ProductId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            errors[nameof(request.Items)] =
                [$"Duplicate ProductId(s): {string.Join(", ", duplicateIds)}. Combine them into a single line item."];
        }

        return errors.Count == 0;
    }
}
