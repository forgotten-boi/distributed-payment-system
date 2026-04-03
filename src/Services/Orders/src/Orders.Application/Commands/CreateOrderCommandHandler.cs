using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;
using Orders.Domain.Aggregates;
using Orders.Domain.Repositories;

namespace Orders.Application.Commands;

/// <summary>
/// Creates a new order and publishes OrderCreatedDomainEvent to trigger the saga.
///
/// The saga (OrderPaymentStateMachine) picks up the domain event
/// and sends the AuthorizePaymentCommand to the Payments service.
///
/// This handler no longer sends bus commands directly — all orchestration
/// is delegated to the saga state machine.
/// </summary>
public class CreateOrderCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IEventBus eventBus,
    ILogger<CreateOrderCommandHandler> logger) : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // Idempotency check
        var existing = await orderRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Duplicate order creation detected for idempotency key {Key}, returning existing order {OrderId}",
                request.IdempotencyKey, existing.Id);
            return new CreateOrderResult(existing.Id, existing.Status.ToString());
        }

        var order = Order.Create(request.CustomerId, request.Amount, request.Currency, request.IdempotencyKey);
        order.StartPaymentAuthorization();

        await orderRepository.AddAsync(order, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}, amount {Amount} {Currency}. Saga will handle authorization.",
            order.Id, request.CustomerId, request.Amount, request.Currency);

        // Publish domain event so the saga receives it immediately
        // (the outbox also captures it, but explicit publish avoids waiting for dispatcher polling)
        await eventBus.PublishAsync(new Orders.Domain.Events.OrderCreatedDomainEvent(
            order.Id, request.CustomerId, request.Amount, request.Currency, request.IdempotencyKey), cancellationToken);

        return new CreateOrderResult(order.Id, order.Status.ToString());
    }
}
