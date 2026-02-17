namespace BuildingBlocks.Persistence;

/// <summary>
/// Outbox message stored in the same transaction as domain state changes.
/// The Outbox Pattern ensures that events are never lost:
///  1. Domain state + OutboxMessage are saved in ONE transaction
///  2. Background worker picks up unprocessed messages
///  3. Worker publishes to RabbitMQ
///  4. Worker marks message as processed
/// If publishing fails, the message stays and is retried.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredOn { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedOn { get; set; }
    public int Retries { get; set; }
    public string? Error { get; set; }
}
