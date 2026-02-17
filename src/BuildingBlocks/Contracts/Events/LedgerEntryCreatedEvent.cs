namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Published when a new ledger entry (or pair of entries) is created in the Accounting service.
/// This provides an audit trail and allows other services to react to financial state changes.
/// </summary>
public record LedgerEntryCreatedEvent(
    Guid LedgerEntryId,
    Guid TransactionId,
    Guid PaymentId,
    string DebitAccount,
    string CreditAccount,
    decimal Amount,
    string Currency,
    string CorrelationId,
    string CausationId,
    DateTime OccurredOn);
