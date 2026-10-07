using System.Net;
using System.Net.Http.Json;

namespace OrderService.IntegrationTests;

/// <summary>
/// Truly parallel requests against real SQL Server. The SQLite unit test can only prove the
/// guard (SQLite serialises writers); these prove the row locking it relies on.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CreateOrderConcurrencyTests(SqlServerApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ParallelBuyersForTheLastUnit_ExactlyOneWins_AndStockNeverGoesNegative()
    {
        await fixture.ExecuteAsync("UPDATE Products SET StockQuantity = 1 WHERE ProductId = 1001");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            fixture.Client.PostAsJsonAsync("/api/orders", new
            {
                customerId = 101,
                items = new[] { new { productId = 1001, quantity = 1 } }
            })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(19, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        Assert.Equal(0, await fixture.ScalarAsync<int>("SELECT StockQuantity FROM Products WHERE ProductId = 1001"));
        Assert.Equal(1, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
    }

    [Fact]
    public async Task ParallelMultiItemOrdersInOppositeItemOrder_AllSucceed_WithoutDeadlock()
    {
        // Half the requests list 1001 then 1002, half 1002 then 1001. Without the handler's
        // ProductId lock ordering this is the textbook deadlock shape.
        await fixture.ExecuteAsync("UPDATE Products SET StockQuantity = 1000 WHERE ProductId IN (1001, 1002)");

        var responses = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
        {
            var items = i % 2 == 0
                ? new[] { new { productId = 1001, quantity = 1 }, new { productId = 1002, quantity = 1 } }
                : new[] { new { productId = 1002, quantity = 1 }, new { productId = 1001, quantity = 1 } };

            return fixture.Client.PostAsJsonAsync("/api/orders", new { customerId = 101, items });
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        Assert.Equal(960, await fixture.ScalarAsync<int>("SELECT StockQuantity FROM Products WHERE ProductId = 1001"));
        Assert.Equal(960, await fixture.ScalarAsync<int>("SELECT StockQuantity FROM Products WHERE ProductId = 1002"));
        Assert.Equal(40, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM Orders WHERE TotalAmount = 3490.00"));
    }
}
