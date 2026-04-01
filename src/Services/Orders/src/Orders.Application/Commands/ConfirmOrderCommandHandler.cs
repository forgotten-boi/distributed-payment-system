using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.Commands;

/// <summary>
/// Confirms an authorized order by publishing ConfirmOrderRequested to the saga.
///
/// The saga state machine receives this message, validates the state transition,
/// and sends a CapturePaymentCommand to the Payments service.
///
/// This handler no longer sends CapturePaymentCommand directly.
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

        logger.LogInformation("Order {OrderId} confirmed, publishing to saga for capture", order.Id);

        // Publish to saga — the state machine will send CapturePaymentCommand
        await eventBus.PublishAsync(new ConfirmOrderRequested(
            OrderId: order.Id,
            CorrelationId: order.Id.ToString(),
            CausationId: order.Id.ToString()), cancellationToken);

        return new ConfirmOrderResult(order.Id, order.Status.ToString());
    }
}
