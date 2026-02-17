using MediatR;

namespace Orders.Application.Queries;

public record GetOrderQuery(Guid OrderId) : IRequest<OrderDto?>;

public record OrderDto(
    Guid Id,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    Guid? PaymentId,
    string? FailureReason,
    DateTime CreatedAt);
