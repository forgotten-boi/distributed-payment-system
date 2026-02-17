using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;
using Orders.Domain.Repositories;

namespace Orders.Application.Commands;

/// <summary>
/// Cancels an order and sends a cancel command to the Payments service.
/// Cancellation releases any holds on customer funds.
/// 
/// Important: Cannot cancel captured orders — only refund flow applies there.
/// This is enforced at the domain level by the Order aggregate.
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

        logger.LogInformation("Order {OrderId} cancelled", order.Id);

        if (order.PaymentId.HasValue)
        {
            await eventBus.SendAsync(new CancelPaymentCommand(
                PaymentId: order.PaymentId.Value,
                OrderId: order.Id,
                IdempotencyKey: $"cancel-{order.Id}",
                CorrelationId: order.Id.ToString(),
                CausationId: order.Id.ToString()), cancellationToken);
        }

        return new CancelOrderResult(order.Id, order.Status.ToString());
    }
}
