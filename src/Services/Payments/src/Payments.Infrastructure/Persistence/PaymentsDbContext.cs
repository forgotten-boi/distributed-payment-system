using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Payments.Domain.Aggregates;

namespace Payments.Infrastructure.Persistence;

public class PaymentsDbContext : OutboxDbContext
{
    public DbSet<Payment> Payments => Set<Payment>();

    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.OrderId).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(500);
            entity.Property(e => e.FailureReason).HasMaxLength(2000);
            entity.Property(e => e.FailureCode).HasMaxLength(100);

            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => e.OrderId);
        });
    }
}
