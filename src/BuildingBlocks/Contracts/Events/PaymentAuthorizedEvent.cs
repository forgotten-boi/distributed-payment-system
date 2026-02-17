namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Published when a payment provider successfully authorizes a payment.
/// Authorization means funds are reserved but not yet transferred.
/// Orders service listens to this to update order status to Authorized.
/// </summary>
public record PaymentAuthorizedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string ProviderTransactionId,
    string CorrelationId,
    string CausationId,
    DateTime OccurredOn);
