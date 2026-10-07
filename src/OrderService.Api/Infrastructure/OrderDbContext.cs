using Microsoft.EntityFrameworkCore;
using OrderService.Api.Domain;

namespace OrderService.Api.Infrastructure;

/// <summary>
/// Maps the existing Part 2 schema. Configuration is explicit rather than
/// convention-driven because the database is the contract here, not the model.
/// </summary>
public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(c => c.CustomerId);
            e.Property(c => c.CustomerName).HasMaxLength(200).IsRequired();
            e.Property(c => c.Email).HasMaxLength(320).IsRequired();
            e.HasIndex(c => c.Email).IsUnique();
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.ProductId);
            e.Property(p => p.ProductName).HasMaxLength(200).IsRequired();
            e.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(o => o.OrderId);
            e.Property(o => o.OrderStatus).HasMaxLength(30).IsUnicode(false).IsRequired();
            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId);
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            e.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("OrderItems");
            e.HasKey(i => i.OrderItemId);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId);
            e.HasIndex(i => i.OrderId);
        });
    }
}
