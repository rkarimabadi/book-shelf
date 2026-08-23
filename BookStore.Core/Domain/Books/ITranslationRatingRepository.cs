namespace BookStore.Core.Domain.Books;

public interface ITranslationRatingRepository
{
    Task<TranslationRating?> GetByUserAndBookAsync(Guid userId, Guid bookId, CancellationToken cancellationToken = default);

    void Add(TranslationRating rating);

    void Update(TranslationRating rating);

    /// <summary>Average and count of ratings for one book; (null, 0) when it has none.</summary>
    Task<(double? Average, int Count)> GetRatingSummaryAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Rating summary for every book that has at least one rating.</summary>
    Task<Dictionary<Guid, (double? Average, int Count)>> GetAllSummariesAsync(CancellationToken cancellationToken = default);
}
