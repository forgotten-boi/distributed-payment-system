namespace BuildingBlocks.Persistence;

/// <summary>
/// Marker interface for domain events raised within an aggregate.
/// Domain events are collected on the aggregate root and dispatched after persistence.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
