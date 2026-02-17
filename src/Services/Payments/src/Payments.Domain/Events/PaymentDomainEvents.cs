using BuildingBlocks.Persistence;

namespace Payments.Domain.Events;

public record PaymentAuthorizedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string ProviderTransactionId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record PaymentCapturedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string ProviderTransactionId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record PaymentFailedDomainEvent(
    Guid PaymentId,
    Guid OrderId,
    string Reason,
    string FailureCode) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record PaymentCancelledDomainEvent(
    Guid PaymentId,
    Guid OrderId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
