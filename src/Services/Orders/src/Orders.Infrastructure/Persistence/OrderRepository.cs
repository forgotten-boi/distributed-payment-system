using Microsoft.EntityFrameworkCore;
using Orders.Domain.Aggregates;
using Orders.Domain.Repositories;

namespace Orders.Infrastructure.Persistence;

public class OrderRepository(OrdersDbContext dbContext) : IOrderRepository
{
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders.FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public void Update(Order order)
    {
        dbContext.Orders.Update(order);
    }
}
