using BookStore.Core.Domain.Translation;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

public sealed class TranslationJobRepository : ITranslationJobRepository
{
    private readonly BookStoreDbContext _dbContext;

    public TranslationJobRepository(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TranslationJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.TranslationJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);
    }

    public Task<List<TranslationJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.TranslationJobs
            .AsNoTracking()
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.TranslationJobs
            .Where(job => job.Status == TranslationJobStatus.Queued || job.Status == TranslationJobStatus.Running)
            .OrderBy(job => job.CreatedAt)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(TranslationJob job)
    {
        _dbContext.TranslationJobs.Add(job);
    }

    public void Update(TranslationJob job)
    {
        _dbContext.TranslationJobs.Update(job);
    }

    public void Delete(TranslationJob job)
    {
        _dbContext.TranslationJobs.Remove(job);
    }
}
