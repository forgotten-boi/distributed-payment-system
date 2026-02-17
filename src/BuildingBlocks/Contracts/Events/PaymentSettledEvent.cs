namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Published when a payment is confirmed as settled by the provider during reconciliation.
/// Settlement means the provider has transferred funds to the merchant's bank account.
/// This is the final state in the payment lifecycle.
/// </summary>
public record PaymentSettledEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string ProviderSettlementId,
    string CorrelationId,
    string CausationId,
    DateTime OccurredOn);
