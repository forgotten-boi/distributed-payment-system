namespace BuildingBlocks.Persistence;

/// <summary>
/// Base class for aggregate roots — the consistency boundary in DDD.
/// Aggregates collect domain events during a business operation.
/// Events are stored in the outbox within the same database transaction
/// as the aggregate state change, guaranteeing atomicity (Outbox Pattern).
/// After the transaction commits, a background dispatcher publishes them to RabbitMQ.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
