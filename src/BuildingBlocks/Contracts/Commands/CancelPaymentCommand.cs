namespace BuildingBlocks.Contracts.Commands;

/// <summary>
/// Command to cancel a previously authorized (but not captured) payment.
/// This releases the hold on the customer's funds.
/// Once a payment is captured, cancellation is not possible — only refunds apply.
/// </summary>
public record CancelPaymentCommand(
    Guid PaymentId,
    Guid OrderId,
    string IdempotencyKey,
    string CorrelationId,
    string CausationId);
