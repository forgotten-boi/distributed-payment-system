namespace Orders.Domain.ValueObjects;

/// <summary>
/// Represents the lifecycle state of an order in the payment pipeline.
/// State transitions are strictly enforced by the Order aggregate:
///
///   Created → PaymentAuthorizing → Authorized → Capturing → Captured
///                                                    ↓
///                                                  Failed
///   Any state before Captured → Cancelled
///
/// Design decision: We use a simple enum rather than a state machine library
/// because the transitions are well-defined and the aggregate enforces them.
/// A state machine would add complexity without proportional benefit here.
/// </summary>
public enum OrderStatus
{
    Created = 0,
    PaymentAuthorizing = 1,
    Authorized = 2,
    Capturing = 3,
    Captured = 4,
    Failed = 5,
    Cancelled = 6
}
