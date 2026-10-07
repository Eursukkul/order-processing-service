using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderService.Api.Domain;
using OrderService.Api.Infrastructure;

namespace OrderService.Tests;

/// <summary>
/// Each test gets its own SQLite in-memory database.
/// <para>
/// SQLite rather than the EF in-memory provider on purpose: in-memory is not a
/// relational store, so it silently ignores transactions and <c>ExecuteUpdate</c>
/// - the two mechanisms this handler depends on. SQLite executes real SQL, so a
/// green test means the SQL shape is actually valid.
/// </para>
/// </summary>
public sealed class OrderDbContextFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public OrderDbContextFixture()
    {
        // The database lives as long as the connection is open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Context = CreateContext();
        Context.Database.EnsureCreated();
    }

    public OrderDbContext Context { get; }

    public OrderDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlite(_connection)
            .Options);

    /// <summary>Seeds the fixed catalogue the tests assert against.</summary>
    public OrderDbContextFixture Seed()
    {
        Context.Customers.Add(new Customer
        {
            CustomerId = 101,
            CustomerName = "Somchai Jaidee",
            Email = "somchai@example.com",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        Context.Products.AddRange(
            new Product { ProductId = 1001, ProductName = "Mechanical Keyboard", Price = 2500.00m, StockQuantity = 10, IsActive = true },
            new Product { ProductId = 1002, ProductName = "USB-C Hub", Price = 990.00m, StockQuantity = 5, IsActive = true },
            new Product { ProductId = 1003, ProductName = "Discontinued Mouse", Price = 450.00m, StockQuantity = 3, IsActive = false },
            new Product { ProductId = 1004, ProductName = "Sold Out Dock", Price = 1800.00m, StockQuantity = 0, IsActive = true });

        Context.SaveChanges();
        Context.ChangeTracker.Clear();
        return this;
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
