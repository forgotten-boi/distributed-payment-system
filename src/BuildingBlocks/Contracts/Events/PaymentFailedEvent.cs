namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Published when a payment fails at any stage (authorization, capture, provider error).
/// This is a compensating event — no rollback, only explicit failure handling.
/// Orders service listens to this to mark the order as failed and potentially retry.
/// </summary>
public record PaymentFailedEvent(
    Guid PaymentId,
    Guid OrderId,
    string Reason,
    string FailureCode,
    string CorrelationId,
    string CausationId,
    DateTime OccurredOn);
