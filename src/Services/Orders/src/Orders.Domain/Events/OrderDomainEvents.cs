using BuildingBlocks.Persistence;

namespace Orders.Domain.Events;

public record OrderCreatedDomainEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string IdempotencyKey) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record OrderPaymentAuthorizingDomainEvent(
    Guid OrderId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record OrderAuthorizedDomainEvent(
    Guid OrderId,
    Guid PaymentId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record OrderCapturedDomainEvent(
    Guid OrderId,
    Guid PaymentId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record OrderFailedDomainEvent(
    Guid OrderId,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record OrderCancelledDomainEvent(
    Guid OrderId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
