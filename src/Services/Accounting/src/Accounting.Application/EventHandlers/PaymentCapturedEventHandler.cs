using Accounting.Domain.Entities;
using Accounting.Domain.Repositories;
using Accounting.Domain.ValueObjects;
using BuildingBlocks.Contracts.Events;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Accounting.Application.EventHandlers;

/// <summary>
/// Creates double-entry ledger records when a payment is captured.
///
/// This is the financial heart of the system. When PaymentCaptured arrives:
///
///   Debit:  CustomerReceivable  $100.00
///   Credit: Revenue             $100.00
///
/// Meaning:
///   - We have a receivable (asset) from the customer for $100
///   - We recognize $100 in revenue
///
/// The double-entry guarantee:
///   Sum of all debits == Sum of all credits (always, for every transaction)
///
/// If this invariant is ever violated, the reconciliation job will detect it.
///
/// Both entries share the same TransactionId, linking them as a pair.
/// The entries are immutable — corrections create new compensating entries.
/// </summary>
public class PaymentCapturedEventHandler(
    ILedgerRepository ledgerRepository,
    IUnitOfWork unitOfWork,
    ILogger<PaymentCapturedEventHandler> logger) : IConsumer<PaymentCapturedEvent>
{
    public async Task Consume(ConsumeContext<PaymentCapturedEvent> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Creating ledger entries for PaymentCaptured: payment {PaymentId}, order {OrderId}, amount {Amount} {Currency}",
            message.PaymentId, message.OrderId, message.Amount, message.Currency);

        // Check for existing entries (idempotency)
        var existing = await ledgerRepository.GetByPaymentIdAsync(message.PaymentId, context.CancellationToken);
        if (existing.Count > 0)
        {
            logger.LogInformation("Ledger entries already exist for payment {PaymentId}, skipping", message.PaymentId);
            return;
        }

        var transactionId = Guid.NewGuid();

        var debitEntry = LedgerEntry.CreateDebit(
            transactionId: transactionId,
            paymentId: message.PaymentId,
            accountName: Accounts.CustomerReceivable,
            amount: message.Amount,
            currency: message.Currency,
            description: $"Payment capture for order {message.OrderId}");

        var creditEntry = LedgerEntry.CreateCredit(
            transactionId: transactionId,
            paymentId: message.PaymentId,
            accountName: Accounts.Revenue,
            amount: message.Amount,
            currency: message.Currency,
            description: $"Revenue from order {message.OrderId}");

        await ledgerRepository.AddRangeAsync([debitEntry, creditEntry], context.CancellationToken);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Ledger entries created: txn {TransactionId} — Debit {DebitAccount} / Credit {CreditAccount} {Amount} {Currency}",
            transactionId, Accounts.CustomerReceivable, Accounts.Revenue, message.Amount, message.Currency);
    }
}
