using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.Commands;

/// <summary>
/// Cancels an order by publishing CancelOrderRequested to the saga.
///
/// The saga state machine receives this message, determines if a payment
/// hold needs to be released, and sends CancelPaymentCommand if needed.
///
/// Cannot cancel captured orders — enforced at the domain level.
/// </summary>
public class CancelOrderCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IEventBus eventBus,
    ILogger<CancelOrderCommandHandler> logger) : IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    public async Task<CancelOrderResult> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new DomainException($"Order {request.OrderId} not found.", "ORDER_NOT_FOUND");

        order.Cancel();

        orderRepository.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId} cancelled, publishing to saga", order.Id);

        // Publish to saga — the state machine will send CancelPaymentCommand if needed
        await eventBus.PublishAsync(new CancelOrderRequested(
            OrderId: order.Id,
            CorrelationId: order.Id.ToString(),
            CausationId: order.Id.ToString()), cancellationToken);

        return new CancelOrderResult(order.Id, order.Status.ToString());
    }
}
