namespace BuildingBlocks.Contracts.Commands;

/// <summary>
/// Saga trigger: the user has requested cancellation of an order.
/// Published by the Orders API and consumed by the OrderPaymentStateMachine.
/// The saga will send a CancelPaymentCommand if a payment is in progress.
/// </summary>
public record CancelOrderRequested(
    Guid OrderId,
    string CorrelationId,
    string CausationId);
