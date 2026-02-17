namespace BuildingBlocks.Messaging;

/// <summary>
/// Abstraction over the message bus (RabbitMQ via MassTransit).
/// Services publish integration events through this interface.
/// The implementation handles serialization, routing, and delivery guarantees.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
    Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
}
