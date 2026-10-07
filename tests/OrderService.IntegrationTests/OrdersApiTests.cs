using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OrderService.IntegrationTests;

/// <summary>
/// The HTTP contract end to end on real SQL Server: status codes, ProblemDetails, the
/// Location header, and values that only round-trip correctly on SQL Server.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OrdersApiTests(SqlServerApiFixture fixture) : IAsyncLifetime
{
    private const string ProblemJson = "application/problem+json";

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostThenFollowLocation_ReturnsThePersistedOrderWithUtcTimestamps()
    {
        // Runs the explicit transaction under SqlServerRetryingExecutionStrategy, which throws
        // at runtime if the transaction is not wrapped in CreateExecutionStrategy().
        var created = await fixture.Client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 101,
            items = new[] { new { productId = 1001, quantity = 2 }, new { productId = 1002, quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var order = await fixture.Client.GetFromJsonAsync<JsonElement>(created.Headers.Location);

        Assert.Equal("Pending", order.GetProperty("orderStatus").GetString());
        Assert.Equal(5990.00m, order.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2, order.GetProperty("items").GetArrayLength());

        // DATETIME2 comes back from SQL Server as DateTimeKind.Unspecified; the API must still
        // emit UTC ("...Z"), or clients read it as local time.
        Assert.EndsWith("Z", order.GetProperty("createdAt").GetString());
        Assert.EndsWith("Z", order.GetProperty("updatedAt").GetString());

        Assert.Equal(8, await fixture.ScalarAsync<int>("SELECT StockQuantity FROM Products WHERE ProductId = 1001"));
        Assert.Equal(4, await fixture.ScalarAsync<int>("SELECT StockQuantity FROM Products WHERE ProductId = 1002"));
    }

    [Fact]
    public async Task OneShortItem_RollsBackTheWholeOrder()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 101,
            items = new[] { new { productId = 1001, quantity = 1 }, new { productId = 1002, quantity = 500 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, await fixture.ScalarAsync<int>("SELECT StockQuantity FROM Products WHERE ProductId = 1001"));
        Assert.Equal(0, await fixture.ScalarAsync<int>("SELECT COUNT(*) FROM Orders"));
    }

    [Theory]
    [InlineData("""{"customerId":101,"items":[{"productId":1002,"quantity":50}]}""", HttpStatusCode.Conflict)]
    [InlineData("""{"customerId":101,"items":[{"productId":1003,"quantity":1}]}""", HttpStatusCode.Conflict)]
    [InlineData("""{"customerId":999999,"items":[{"productId":1001,"quantity":1}]}""", HttpStatusCode.NotFound)]
    [InlineData("""{"customerId":101,"items":[{"productId":777777,"quantity":1}]}""", HttpStatusCode.NotFound)]
    [InlineData("""{"customerId":101,"items":[{"productId":1001,"quantity":0}]}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"customerId":101,"items":[null]}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"customerId":""", HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public async Task Rejections_ReturnTheExpectedStatus_AsProblemDetails(string body, HttpStatusCode expected)
    {
        var response = await fixture.Client.PostAsync("/api/orders",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task GetUnknownOrder_Returns404ProblemDetails()
    {
        var response = await fixture.Client.GetAsync("/api/orders/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }
}
