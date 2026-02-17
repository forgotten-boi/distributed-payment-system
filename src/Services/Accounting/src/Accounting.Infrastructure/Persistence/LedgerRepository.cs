using Accounting.Domain.Entities;
using Accounting.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Persistence;

public class LedgerRepository(AccountingDbContext dbContext) : ILedgerRepository
{
    public async Task<List<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
        => await dbContext.LedgerEntries
            .Where(e => e.TransactionId == transactionId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<List<LedgerEntry>> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => await dbContext.LedgerEntries
            .Where(e => e.PaymentId == paymentId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<List<LedgerEntry>> GetByAccountNameAsync(string accountName, CancellationToken cancellationToken = default)
        => await dbContext.LedgerEntries
            .Where(e => e.AccountName == accountName)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default)
        => await dbContext.LedgerEntries.AddRangeAsync(entries, cancellationToken);
}
