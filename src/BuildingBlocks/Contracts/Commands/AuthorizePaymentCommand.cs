namespace BuildingBlocks.Contracts.Commands;

/// <summary>
/// Command to authorize a payment for an order.
/// Sent from Orders service to Payments service when a customer initiates a payment.
/// The IdempotencyKey ensures that duplicate submissions do not result in double charges.
/// </summary>
public record AuthorizePaymentCommand(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string CorrelationId,
    string CausationId);
