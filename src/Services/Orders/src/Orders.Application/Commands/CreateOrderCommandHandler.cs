using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;
using Orders.Domain.Aggregates;
using Orders.Domain.Repositories;

namespace Orders.Application.Commands;

/// <summary>
/// Creates a new order and sends AuthorizePayment command to the Payments service.
///
/// Flow:
///  1. Check idempotency — if order with same key exists, return it
///  2. Create Order aggregate (validates business rules)
///  3. Transition to PaymentAuthorizing state
///  4. Save to DB (domain events → outbox atomically)
///  5. Send AuthorizePayment command to Payments service
///
/// The AuthorizePayment command is sent via the event bus (not outbox) because
/// commands are point-to-point. The order state change is protected by the outbox.
/// If the command send fails, the order stays in PaymentAuthorizing and can be retried.
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
            "Order {OrderId} created for customer {CustomerId}, amount {Amount} {Currency}. Sending authorization.",
            order.Id, request.CustomerId, request.Amount, request.Currency);

        // Send command to Payments service
        await eventBus.SendAsync(new AuthorizePaymentCommand(
            OrderId: order.Id,
            Amount: request.Amount,
            Currency: request.Currency,
            IdempotencyKey: request.IdempotencyKey,
            CorrelationId: order.Id.ToString(),
            CausationId: order.Id.ToString()), cancellationToken);

        return new CreateOrderResult(order.Id, order.Status.ToString());
    }
}
