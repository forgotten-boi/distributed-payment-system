using MassTransit;

namespace Orders.Application.Saga;

/// <summary>
/// Saga instance state — persisted in the Orders database.
///
/// Each OrderPaymentState row represents one order's journey through the
/// payment lifecycle. The saga state machine governs all transitions.
///
/// The CorrelationId IS the OrderId — this ensures one saga instance per order.
///
/// MassTransit requires:
///   - CorrelationId (Guid) for routing messages to the correct instance
///   - CurrentState (string) for the serialized state name
///   - All custom fields the saga needs to send compensating commands
/// </summary>
public class OrderPaymentState : SagaStateMachineInstance
{
    /// <summary>OrderId — used as the saga correlation identifier.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Serialized state name (e.g., "Authorizing", "Authorized", "Capturing").</summary>
    public string CurrentState { get; set; } = string.Empty;

    // ── Order data (captured from the initial event) ──

    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    // ── Payment data (populated as the saga progresses) ──

    public Guid? PaymentId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? FailureReason { get; set; }

    // ── Timestamps ──

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Timeout token (for scheduled authorization timeout) ──

    public Guid? AuthorizationTimeoutTokenId { get; set; }
}
