using BuildingBlocks.Exceptions;
using BuildingBlocks.Persistence;

namespace Accounting.Domain.Entities;

/// <summary>
/// Represents a single entry in the double-entry ledger.
/// 
/// Double-entry accounting principle:
///   Every financial transaction creates exactly TWO entries:
///   1. A DEBIT entry (money coming in / asset increasing)
///   2. A CREDIT entry (money going out / liability/revenue increasing)
///   
///   For a payment capture, we create:
///   - Debit:  CustomerReceivable (asset — we are owed this money)
///   - Credit: Revenue (revenue — we earned this money)
///
/// The sum of all debits must ALWAYS equal the sum of all credits.
/// This invariant is the foundation of financial correctness.
///
/// LedgerEntries are immutable once created — corrections are made by
/// creating new compensating entries, never by modifying existing ones.
/// This provides a complete audit trail.
/// </summary>
public class LedgerEntry : Entity
{
    /// <summary>
    /// Groups related entries. All entries in a double-entry pair share this ID.
    /// </summary>
    public Guid TransactionId { get; private set; }

    /// <summary>
    /// Reference to the payment that triggered this entry.
    /// </summary>
    public Guid PaymentId { get; private set; }

    /// <summary>
    /// The account this entry affects (e.g., "CustomerReceivable", "Revenue").
    /// </summary>
    public string AccountName { get; private set; } = string.Empty;

    /// <summary>
    /// Amount debited. Zero if this is a credit entry.
    /// </summary>
    public decimal DebitAmount { get; private set; }

    /// <summary>
    /// Amount credited. Zero if this is a debit entry.
    /// </summary>
    public decimal CreditAmount { get; private set; }

    /// <summary>
    /// Currency code (ISO 4217).
    /// </summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>
    /// Human-readable description of this entry.
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    private LedgerEntry() { } // EF Core

    public static LedgerEntry CreateDebit(
        Guid transactionId, Guid paymentId, string accountName,
        decimal amount, string currency, string description)
    {
        ValidateAmount(amount);
        return new LedgerEntry
        {
            TransactionId = transactionId,
            PaymentId = paymentId,
            AccountName = accountName,
            DebitAmount = amount,
            CreditAmount = 0,
            Currency = currency,
            Description = description
        };
    }

    public static LedgerEntry CreateCredit(
        Guid transactionId, Guid paymentId, string accountName,
        decimal amount, string currency, string description)
    {
        ValidateAmount(amount);
        return new LedgerEntry
        {
            TransactionId = transactionId,
            PaymentId = paymentId,
            AccountName = accountName,
            DebitAmount = 0,
            CreditAmount = amount,
            Currency = currency,
            Description = description
        };
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Ledger entry amount must be positive.", "INVALID_LEDGER_AMOUNT");
    }
}
