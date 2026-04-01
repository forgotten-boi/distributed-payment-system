namespace BuildingBlocks.Contracts.Events;

/// <summary>
/// Scheduled timeout event: if authorization takes too long,
/// the saga fires this to transition the order to Failed state.
/// This prevents orders stuck forever in Authorizing state.
/// </summary>
public record AuthorizationTimeoutExpired(
    Guid OrderId,
    string CorrelationId,
    DateTime OccurredOn);
