namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Published when funds are successfully captured (transferred) from the customer.
/// This is the point of no return — money has moved.
/// Accounting service listens to this to create double-entry ledger records.
/// Orders service listens to this to finalize the order.
/// </summary>
public record PaymentCapturedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string ProviderTransactionId,
    string CorrelationId,
    string CausationId,
    DateTime OccurredOn);
