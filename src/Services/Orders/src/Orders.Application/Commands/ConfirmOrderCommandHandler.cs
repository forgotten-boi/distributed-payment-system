using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.Commands;

/// <summary>
/// Confirms an authorized order and triggers payment capture.
///
/// This represents the business decision to fulfill the order.
/// In a real system this might happen after inventory check, fraud review, etc.
///
/// Flow:
///  1. Load order, validate it's in Authorized state
///  2. Transition to Capturing
///  3. Save state change (with outbox)
///  4. Send CapturePayment command to Payments service
/// </summary>
public class ConfirmOrderCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IEventBus eventBus,
    ILogger<ConfirmOrderCommandHandler> logger) : IRequestHandler<ConfirmOrderCommand, ConfirmOrderResult>
{
    public async Task<ConfirmOrderResult> Handle(ConfirmOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new DomainException($"Order {request.OrderId} not found.", "ORDER_NOT_FOUND");

        order.StartCapture();

        orderRepository.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId} confirmed, triggering capture for payment {PaymentId}",
            order.Id, order.PaymentId);

        await eventBus.SendAsync(new CapturePaymentCommand(
            PaymentId: order.PaymentId!.Value,
            OrderId: order.Id,
            IdempotencyKey: $"capture-{order.Id}",
            CorrelationId: order.Id.ToString(),
            CausationId: order.Id.ToString()), cancellationToken);

        return new ConfirmOrderResult(order.Id, order.Status.ToString());
    }
}
