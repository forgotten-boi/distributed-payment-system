namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Published by the saga whenever it transitions states.
/// Allows external systems (WebUI, monitoring) to track saga progress
/// without querying the saga persistence table directly.
/// </summary>
public record OrderSagaStateChanged(
    Guid OrderId,
    string PreviousState,
    string CurrentState,
    string? PaymentId,
    string? FailureReason,
    string CorrelationId,
    DateTime OccurredOn);
