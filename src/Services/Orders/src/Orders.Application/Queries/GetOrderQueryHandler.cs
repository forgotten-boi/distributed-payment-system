using MediatR;
using Orders.Domain.Repositories;

namespace Orders.Application.Queries;

public class GetOrderQueryHandler(IOrderRepository orderRepository)
    : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null) return null;

        return new OrderDto(
            order.Id,
            order.CustomerId,
            order.Amount,
            order.Currency,
            order.Status.ToString(),
            order.PaymentId,
            order.FailureReason,
            order.CreatedAt);
    }
}
