namespace BuildingBlocks.Contracts.Commands;

/// <summary>
/// Saga trigger: the user has confirmed an authorized order.
/// Published by the Orders API and consumed by the OrderPaymentStateMachine.
/// The saga will then send a CapturePaymentCommand to the Payments service.
/// </summary>
public record ConfirmOrderRequested(
    Guid OrderId,
    string CorrelationId,
    string CausationId);
