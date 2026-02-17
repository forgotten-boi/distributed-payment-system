namespace Accounting.Domain.ValueObjects;

/// <summary>
/// Well-known account names in the ledger.
/// Using constants prevents typos in account names across the system.
/// </summary>
public static class Accounts
{
    public const string CustomerReceivable = "CustomerReceivable";
    public const string Revenue = "Revenue";
    public const string SettlementClearing = "SettlementClearing";
    public const string AdjustmentExpense = "AdjustmentExpense";
}
