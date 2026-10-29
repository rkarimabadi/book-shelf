namespace BookStore.Core.Domain.Translation;

public interface IAiProviderSettingsRepository
{
    /// <summary>The single settings row, or null when the admin has not saved any yet.</summary>
    Task<AiProviderSettings?> GetAsync(CancellationToken cancellationToken = default);

    void Add(AiProviderSettings settings);

    void Update(AiProviderSettings settings);
}
