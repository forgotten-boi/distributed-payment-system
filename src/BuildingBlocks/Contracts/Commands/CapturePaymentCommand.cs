namespace BuildingBlocks.Contracts.Commands;

/// <summary>
/// Command to capture funds for a previously authorized payment.
/// Sent when the order is confirmed and goods/services are ready for delivery.
/// Capture is the step that actually moves money — authorization only reserves it.
/// </summary>
public record CapturePaymentCommand(
    Guid PaymentId,
    Guid OrderId,
    string IdempotencyKey,
    string CorrelationId,
    string CausationId);
