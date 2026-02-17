using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Base DbContext that provides outbox and idempotency support.
/// All service-specific DbContexts inherit from this to get automatic
/// domain event → outbox message conversion on SaveChanges.
/// 
/// Flow:
///  1. Service modifies aggregate (e.g., Payment.Authorize())
///  2. Aggregate raises domain events internally
///  3. SaveChangesAsync intercepts, converts events to OutboxMessages
///  4. Both aggregate state and OutboxMessages are saved in ONE transaction
///  5. Background dispatcher reads OutboxMessages and publishes to RabbitMQ
/// </summary>
public abstract class OutboxDbContext : DbContext, IUnitOfWork
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedCommand> ProcessedCommands => Set<ProcessedCommand>();

    protected OutboxDbContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Payload).IsRequired();
            entity.HasIndex(e => e.ProcessedOn);
        });

        modelBuilder.Entity<ProcessedCommand>(entity =>
        {
            entity.ToTable("ProcessedCommands");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(500);
            entity.Property(e => e.Response).IsRequired();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ConvertDomainEventsToOutboxMessages();
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void ConvertDomainEventsToOutboxMessages()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count != 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    OccurredOn = domainEvent.OccurredOn,
                    Type = domainEvent.GetType().AssemblyQualifiedName ?? domainEvent.GetType().FullName ?? domainEvent.GetType().Name,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
                };

                OutboxMessages.Add(outboxMessage);
            }

            aggregate.ClearDomainEvents();
        }
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<Entity>()
            .Where(e => e.State is EntityState.Modified);

        foreach (var entry in entries)
        {
            entry.Entity.GetType().GetProperty(nameof(Entity.UpdatedAt))
                ?.SetValue(entry.Entity, DateTime.UtcNow);
        }
    }
}
