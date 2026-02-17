using MediatR;

namespace Orders.Application.Commands;

public record CreateOrderCommand(
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string IdempotencyKey) : IRequest<CreateOrderResult>;

public record CreateOrderResult(
    Guid OrderId,
    string Status);
