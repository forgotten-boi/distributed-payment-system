namespace Payments.Domain.ValueObjects;

/// <summary>
/// Payment lifecycle states.
/// 
///   Pending → Authorized → Captured → Settled
///                ↓             ↓
///              Failed        Failed
///                ↓
///            Cancelled
///
/// - Pending: payment created, awaiting provider response
/// - Authorized: funds reserved on customer's instrument
/// - Captured: funds transferred from customer
/// - Settled: provider confirmed settlement to merchant bank
/// - Failed: any stage failure (compensating event emitted)
/// - Cancelled: authorized payment voided before capture
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Failed = 3,
    Cancelled = 4,
    Settled = 5
}
