using Accounting.Domain.Entities;

namespace Accounting.Domain.Repositories;

public interface ILedgerRepository
{
    Task<List<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default);
    Task<List<LedgerEntry>> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<List<LedgerEntry>> GetByAccountNameAsync(string accountName, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default);
}
