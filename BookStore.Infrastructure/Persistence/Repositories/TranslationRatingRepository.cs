using BookStore.Core.Domain.Books;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

public sealed class TranslationRatingRepository : ITranslationRatingRepository
{
    private readonly BookStoreDbContext _dbContext;

    public TranslationRatingRepository(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TranslationRating?> GetByUserAndBookAsync(Guid userId, Guid bookId, CancellationToken cancellationToken = default)
    {
        return _dbContext.TranslationRatings
            .FirstOrDefaultAsync(rating => rating.UserId == userId && rating.BookId == bookId, cancellationToken);
    }

    public void Add(TranslationRating rating)
    {
        _dbContext.TranslationRatings.Add(rating);
    }

    public void Update(TranslationRating rating)
    {
        _dbContext.TranslationRatings.Update(rating);
    }

    public async Task<(double? Average, int Count)> GetRatingSummaryAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var summary = await _dbContext.TranslationRatings
            .Where(rating => rating.BookId == bookId)
            .GroupBy(rating => rating.BookId)
            .Select(group => new { Average = group.Average(x => (double?)x.Rating), Count = group.Count() })
            .FirstOrDefaultAsync(cancellationToken);

        return (summary?.Average, summary?.Count ?? 0);
    }

    public async Task<Dictionary<Guid, (double? Average, int Count)>> GetAllSummariesAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await _dbContext.TranslationRatings
            .GroupBy(rating => rating.BookId)
            .Select(group => new { group.Key, Average = group.Average(x => (double?)x.Rating), Count = group.Count() })
            .ToListAsync(cancellationToken);

        return summaries.ToDictionary(summary => summary.Key, summary => (summary.Average, summary.Count));
    }
}
