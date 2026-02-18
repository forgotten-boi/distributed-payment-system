namespace WebUI.Models;

// ── Request Models ──

public sealed record CreateOrderRequest(
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string IdempotencyKey);

// ── Response Models ──

public sealed record OrderResult(Guid OrderId, string Status);

public sealed record OrderDetail(
    Guid Id,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    Guid? PaymentId,
    string? FailureReason);

public sealed record PaymentDetail(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt);

public sealed record LedgerEntryDetail(
    Guid Id,
    Guid TransactionId,
    Guid PaymentId,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    string Currency,
    string Description,
    DateTime CreatedAt);

public sealed record AccountBalance(
    string Account,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal NetBalance,
    int EntryCount);

public sealed record ReconciliationResult(
    bool IsBalanced,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Difference,
    int EntryCount);
