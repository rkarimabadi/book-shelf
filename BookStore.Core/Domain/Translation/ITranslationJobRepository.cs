namespace BookStore.Core.Domain.Translation;

public interface ITranslationJobRepository
{
    Task<TranslationJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All jobs, newest first.</summary>
    Task<List<TranslationJob>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Ids of queued or running jobs, oldest first (resumed after a restart).</summary>
    Task<List<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken = default);

    void Add(TranslationJob job);

    void Update(TranslationJob job);

    void Delete(TranslationJob job);
}
