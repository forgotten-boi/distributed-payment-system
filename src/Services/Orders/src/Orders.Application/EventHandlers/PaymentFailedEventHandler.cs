using BuildingBlocks.Contracts.Events;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.EventHandlers;

/// <summary>
/// Handles PaymentFailedEvent from the Payments service.
/// When a payment fails (provider decline, timeout, fraud check failure),
/// the order is marked as failed with the specific reason.
///
/// Important: We do NOT retry automatically here. The failure is a fact.
/// If the business wants to retry, they create a NEW order with a new
/// idempotency key. This prevents silent loops of failed charges.
/// </summary>
public class PaymentFailedEventHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    ILogger<PaymentFailedEventHandler> logger) : IConsumer<PaymentFailedEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Received PaymentFailed for order {OrderId}: {Reason}",
            message.OrderId, message.Reason);

        var order = await orderRepository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found for PaymentFailed event", message.OrderId);
            return;
        }

        order.MarkFailed(message.Reason);
        orderRepository.Update(order);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Order {OrderId} marked as failed: {Reason}", message.OrderId, message.Reason);
    }
}
