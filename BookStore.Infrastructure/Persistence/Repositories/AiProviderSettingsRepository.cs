using BookStore.Core.Domain.Translation;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

public sealed class AiProviderSettingsRepository : IAiProviderSettingsRepository
{
    private readonly BookStoreDbContext _dbContext;

    public AiProviderSettingsRepository(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AiProviderSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        // A single row; the oldest one wins should a race ever insert two.
        return _dbContext.AiProviderSettings
            .OrderBy(settings => settings.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(AiProviderSettings settings)
    {
        _dbContext.AiProviderSettings.Add(settings);
    }

    public void Update(AiProviderSettings settings)
    {
        _dbContext.AiProviderSettings.Update(settings);
    }
}
