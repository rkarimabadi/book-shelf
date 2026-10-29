using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;

namespace BookStore.Application.Features.AiSettings.Common;

/// <summary>Stored AI settings plus the decrypted connection used to call the provider.</summary>
public sealed record ResolvedAiSettings(AiProviderSettings Settings, AiConnection Connection);

/// <summary>Loads the saved AI provider settings and decrypts the API key.</summary>
public sealed class AiConnectionResolver
{
    private readonly IAiProviderSettingsRepository _settingsRepository;
    private readonly ISecretProtector _secretProtector;

    public AiConnectionResolver(IAiProviderSettingsRepository settingsRepository, ISecretProtector secretProtector)
    {
        _settingsRepository = settingsRepository;
        _secretProtector = secretProtector;
    }

    public async Task<ErrorOr<ResolvedAiSettings>> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        if (settings is null)
        {
            return AiProviderSettingsErrors.NotConfigured;
        }

        string? apiKey = null;
        if (settings.HasApiKey)
        {
            // A key that no longer decrypts (DataProtection keys lost) must be entered again.
            apiKey = _secretProtector.Unprotect(settings.ProtectedApiKey!);
            if (apiKey is null)
            {
                return AiProviderSettingsErrors.ApiKeyMissing;
            }
        }

        return new ResolvedAiSettings(
            settings,
            new AiConnection(settings.Provider, settings.Endpoint, apiKey, settings.Model));
    }
}
