using MassTransit;

namespace BuildingBlocks.Messaging;

/// <summary>
/// MassTransit-backed implementation of IEventBus.
/// Uses RabbitMQ as transport, with automatic exchange/queue topology.
/// Publish = fan-out to all subscribers (events).
/// Send = point-to-point to a specific consumer (commands).
/// </summary>
public class MassTransitEventBus(IPublishEndpoint publishEndpoint, ISendEndpointProvider sendEndpointProvider) : IEventBus
{
    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        await publishEndpoint.Publish(message, cancellationToken);
    }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        var endpoint = await sendEndpointProvider.GetSendEndpoint(
            new Uri($"queue:{typeof(T).Name}"));
        await endpoint.Send(message, cancellationToken);
    }
}
