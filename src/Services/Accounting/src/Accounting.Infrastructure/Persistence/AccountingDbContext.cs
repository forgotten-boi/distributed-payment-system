using Accounting.Domain.Entities;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Persistence;

public class AccountingDbContext : OutboxDbContext
{
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public AccountingDbContext(DbContextOptions<AccountingDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("LedgerEntries");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TransactionId).IsRequired();
            entity.Property(e => e.PaymentId).IsRequired();
            entity.Property(e => e.AccountName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DebitAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(e => e.CreditAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasIndex(e => e.TransactionId);
            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => e.AccountName);
        });
    }
}
