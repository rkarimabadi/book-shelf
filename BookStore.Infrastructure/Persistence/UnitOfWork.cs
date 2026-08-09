using BookStore.Application.Common.Interfaces;

namespace BookStore.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly BookStoreDbContext _dbContext;

    public UnitOfWork(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
