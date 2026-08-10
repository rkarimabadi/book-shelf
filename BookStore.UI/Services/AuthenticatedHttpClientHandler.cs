using System.Net.Http.Headers;

namespace BookStore.UI.Services;

/// <summary>
/// Attaches the stored JWT access token as a Bearer header to every request,
/// so authenticated API endpoints work without scattering token logic in components.
/// </summary>
public sealed class AuthenticatedHttpClientHandler : DelegatingHandler
{
    private readonly IClientStorageService _storage;

    public AuthenticatedHttpClientHandler(IClientStorageService storage)
    {
        _storage = storage;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _storage.GetItemAsync<string>(AuthStateProvider.TokenStorageKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
