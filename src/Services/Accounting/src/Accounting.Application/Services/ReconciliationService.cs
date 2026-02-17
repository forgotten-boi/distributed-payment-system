using Accounting.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Accounting.Application.Services;

/// <summary>
/// Reconciliation service — compares captured payments against settled amounts.
///
/// In a real system, this would:
///  1. Download the provider's settlement report (daily/nightly)
///  2. Compare each settlement line with our captured payments
///  3. For matches: mark payment as settled
///  4. For discrepancies: create adjustment ledger entries
///
/// Discrepancy types:
///  - Provider settled more than we captured → Adjustment credit
///  - Provider settled less than we captured → Adjustment debit
///  - Provider settled payment we don't have → Flag for investigation
///  - We have captured payment provider didn't settle → Flag for follow-up
///
/// This is a simplified implementation that validates ledger integrity.
/// </summary>
public class ReconciliationService(
    ILedgerRepository ledgerRepository,
    ILogger<ReconciliationService> logger)
{
    public async Task<ReconciliationResult> RunReconciliationAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting reconciliation run");

        // Validate double-entry integrity: sum of debits must equal sum of credits
        var allDebitEntries = await ledgerRepository.GetByAccountNameAsync("CustomerReceivable", cancellationToken);
        var allCreditEntries = await ledgerRepository.GetByAccountNameAsync("Revenue", cancellationToken);

        var totalDebits = allDebitEntries.Sum(e => e.DebitAmount);
        var totalCredits = allCreditEntries.Sum(e => e.CreditAmount);

        var isBalanced = totalDebits == totalCredits;

        if (!isBalanced)
        {
            logger.LogError(
                "RECONCILIATION FAILURE: Ledger is unbalanced! Debits={Debits}, Credits={Credits}, Difference={Diff}",
                totalDebits, totalCredits, totalDebits - totalCredits);
        }
        else
        {
            logger.LogInformation(
                "Reconciliation passed: Debits={Debits}, Credits={Credits} — balanced",
                totalDebits, totalCredits);
        }

        return new ReconciliationResult(
            IsBalanced: isBalanced,
            TotalDebits: totalDebits,
            TotalCredits: totalCredits,
            Difference: totalDebits - totalCredits,
            EntryCount: allDebitEntries.Count + allCreditEntries.Count,
            RunAt: DateTime.UtcNow);
    }
}

public record ReconciliationResult(
    bool IsBalanced,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Difference,
    int EntryCount,
    DateTime RunAt);
