using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Orders.Domain.Aggregates;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// Orders-specific DbContext.
/// Inherits from OutboxDbContext to get automatic outbox + idempotency support.
/// Each service owns its own database — no shared schemas across services.
/// </summary>
public class OrdersDbContext : OutboxDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CustomerId).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(500).IsRequired();
            entity.Property(e => e.FailureReason).HasMaxLength(2000);

            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
        });
    }
}
