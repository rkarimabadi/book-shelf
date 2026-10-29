using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using ErrorOr;
using MediatR;

namespace BookStore.Application.Features.AiSettings.Queries.GetAiSettings;

/// <summary>
/// The saved settings (null when none) and a masked hint of the key (its last 4 characters).
/// <c>ApiKeyUnreadable</c> is true when a key is stored but can no longer be decrypted.
/// </summary>
public sealed record AiSettingsResult(
    AiProviderSettings? Settings,
    string? ApiKeyHint,
    bool ApiKeyUnreadable);

public record GetAiSettingsQuery : IRequest<ErrorOr<AiSettingsResult>>;

public class GetAiSettingsQueryHandler : IRequestHandler<GetAiSettingsQuery, ErrorOr<AiSettingsResult>>
{
    private readonly IAiProviderSettingsRepository _settingsRepository;
    private readonly ISecretProtector _secretProtector;

    public GetAiSettingsQueryHandler(IAiProviderSettingsRepository settingsRepository, ISecretProtector secretProtector)
    {
        _settingsRepository = settingsRepository;
        _secretProtector = secretProtector;
    }

    public async Task<ErrorOr<AiSettingsResult>> Handle(GetAiSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        if (settings is null || !settings.HasApiKey)
        {
            return new AiSettingsResult(settings, null, false);
        }

        var key = _secretProtector.Unprotect(settings.ProtectedApiKey!);
        if (key is null)
        {
            return new AiSettingsResult(settings, null, true);
        }

        var hint = key.Length <= 4 ? new string('•', key.Length) : "••••" + key[^4..];
        return new AiSettingsResult(settings, hint, false);
    }
}
