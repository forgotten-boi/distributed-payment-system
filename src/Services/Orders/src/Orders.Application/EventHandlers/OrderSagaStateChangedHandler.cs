using BuildingBlocks.Contracts.Events;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.EventHandlers;

/// <summary>
/// Handles OrderSagaStateChanged events published by the saga state machine.
///
/// This handler synchronizes the Order aggregate state with the saga state,
/// ensuring the local Orders database reflects the latest saga transitions.
///
/// This replaces the previous direct event handlers (PaymentAuthorizedEventHandler,
/// PaymentCapturedEventHandler, PaymentFailedEventHandler) with a single handler
/// that reacts to saga state changes.
/// </summary>
public class OrderSagaStateChangedHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    ILogger<OrderSagaStateChangedHandler> logger) : IConsumer<OrderSagaStateChanged>
{
    public async Task Consume(ConsumeContext<OrderSagaStateChanged> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Saga state changed for Order {OrderId}: {PreviousState} → {CurrentState}",
            message.OrderId, message.PreviousState, message.CurrentState);

        var order = await orderRepository.GetByIdAsync(message.OrderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found for saga state change", message.OrderId);
            return;
        }

        try
        {
            switch (message.CurrentState)
            {
                case "Authorized":
                    if (message.PaymentId is not null && Guid.TryParse(message.PaymentId, out var paymentId))
                    {
                        order.MarkAuthorized(paymentId);
                    }
                    break;

                case "Captured":
                    order.MarkCaptured();
                    break;

                case "Failed":
                    order.MarkFailed(message.FailureReason ?? "Unknown failure");
                    break;

                case "Cancelled":
                    // Order.Cancel() may have already been called by the API handler,
                    // so we only call it if the order isn't already cancelled
                    if (order.Status != Orders.Domain.ValueObjects.OrderStatus.Cancelled)
                    {
                        order.Cancel();
                    }
                    break;

                case "Capturing":
                    // StartCapture may have already been called by the API handler
                    break;

                default:
                    logger.LogWarning("Unknown saga state: {State}", message.CurrentState);
                    return;
            }

            orderRepository.Update(order);
            await unitOfWork.SaveChangesAsync(context.CancellationToken);

            logger.LogInformation("Order {OrderId} synchronized to state {State}",
                message.OrderId, message.CurrentState);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not transition Order {OrderId} to {State} (may already be in that state)",
                message.OrderId, message.CurrentState);
        }
    }
}
