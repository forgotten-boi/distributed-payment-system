namespace BuildingBlocks.Persistence;

/// <summary>
/// Unit of Work abstraction for coordinating aggregate persistence
/// and outbox message storage within a single transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
