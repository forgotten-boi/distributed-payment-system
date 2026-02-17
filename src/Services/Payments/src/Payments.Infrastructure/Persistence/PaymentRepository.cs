using Microsoft.EntityFrameworkCore;
using Payments.Domain.Aggregates;
using Payments.Domain.Repositories;
using Payments.Domain.ValueObjects;

namespace Payments.Infrastructure.Persistence;

public class PaymentRepository(PaymentsDbContext dbContext) : IPaymentRepository
{
    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Payment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => await dbContext.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await dbContext.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public async Task<List<Payment>> GetCapturedNotSettledAsync(CancellationToken cancellationToken = default)
        => await dbContext.Payments
            .Where(p => p.Status == PaymentStatus.Captured)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        => await dbContext.Payments.AddAsync(payment, cancellationToken);

    public void Update(Payment payment)
        => dbContext.Payments.Update(payment);
}
