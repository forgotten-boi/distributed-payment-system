using BuildingBlocks.Contracts.Events;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.EventHandlers;

/// <summary>
/// Handles PaymentAuthorizedEvent from the Payments service.
/// When a payment is successfully authorized, the order transitions to Authorized state.
/// This means funds are reserved on the customer's payment method but not yet captured.
/// The order can now be confirmed (triggering capture) or cancelled (releasing the hold).
/// </summary>
public class PaymentAuthorizedEventHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    ILogger<PaymentAuthorizedEventHandler> logger) : IConsumer<PaymentAuthorizedEvent>
{
    public async Task Consume(ConsumeContext<PaymentAuthorizedEvent> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Received PaymentAuthorized for order {OrderId}, payment {PaymentId}",
            message.OrderId, message.PaymentId);

        var order = await orderRepository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found for PaymentAuthorized event", message.OrderId);
            return;
        }

        order.MarkAuthorized(message.PaymentId);
        orderRepository.Update(order);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Order {OrderId} marked as authorized", message.OrderId);
    }
}
