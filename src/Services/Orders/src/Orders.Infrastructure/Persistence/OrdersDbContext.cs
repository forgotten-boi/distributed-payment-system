using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Orders.Application.Saga;
using Orders.Domain.Aggregates;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// Orders-specific DbContext.
/// Inherits from OutboxDbContext to get automatic outbox + idempotency support.
/// Each service owns its own database — no shared schemas across services.
///
/// Also hosts the saga state table for the OrderPaymentStateMachine.
/// MassTransit uses EF Core to persist saga instances alongside order data.
/// </summary>
public class OrdersDbContext : OutboxDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderPaymentState> OrderPaymentSagaStates => Set<OrderPaymentState>();

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

        // Saga state table — MassTransit persists saga instances here
        modelBuilder.Entity<OrderPaymentState>(entity =>
        {
            entity.ToTable("OrderPaymentSagaStates");
            entity.HasKey(e => e.CorrelationId);

            entity.Property(e => e.CurrentState).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CustomerId).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(500);
            entity.Property(e => e.FailureReason).HasMaxLength(2000);

            entity.HasIndex(e => e.CurrentState);
        });
    }
}
