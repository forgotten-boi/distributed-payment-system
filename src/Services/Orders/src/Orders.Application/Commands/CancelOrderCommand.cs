using MediatR;

namespace Orders.Application.Commands;

public record CancelOrderCommand(Guid OrderId) : IRequest<CancelOrderResult>;

public record CancelOrderResult(Guid OrderId, string Status);
