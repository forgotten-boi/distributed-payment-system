using MediatR;

namespace Orders.Application.Commands;

public record ConfirmOrderCommand(Guid OrderId) : IRequest<ConfirmOrderResult>;

public record ConfirmOrderResult(Guid OrderId, string Status);
