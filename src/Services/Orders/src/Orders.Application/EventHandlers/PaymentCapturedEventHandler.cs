using BuildingBlocks.Contracts.Events;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.EventHandlers;

/// <summary>
/// Handles PaymentCapturedEvent from the Payments service.
/// When funds are successfully captured, the order is finalized.
/// This is the happy-path terminal state — money has moved.
/// </summary>
public class PaymentCapturedEventHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    ILogger<PaymentCapturedEventHandler> logger) : IConsumer<PaymentCapturedEvent>
{
    public async Task Consume(ConsumeContext<PaymentCapturedEvent> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Received PaymentCaptured for order {OrderId}, payment {PaymentId}",
            message.OrderId, message.PaymentId);

        var order = await orderRepository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found for PaymentCaptured event", message.OrderId);
            return;
        }

        order.MarkCaptured();
        orderRepository.Update(order);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Order {OrderId} marked as captured", message.OrderId);
    }
}
