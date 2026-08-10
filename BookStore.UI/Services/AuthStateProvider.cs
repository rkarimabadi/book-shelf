using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BookStore.Contracts.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace BookStore.UI.Services;

public class AuthStateProvider : AuthenticationStateProvider
{
    public const string TokenStorageKey = "auth_token";
    public const string RefreshTokenStorageKey = "refresh_token";

    private readonly IJSRuntime _js;
    private readonly AuthenticationState _anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private AuthenticationState? _cached;

    public AuthStateProvider(IJSRuntime js)
    {
        _js = js;
    }

    public AuthenticationResponse? CurrentSession { get; private set; }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var token = await _js.InvokeAsync<string>("localStorage.getItem", TokenStorageKey);
        var refreshToken = await _js.InvokeAsync<string>("localStorage.getItem", RefreshTokenStorageKey);

        var principal = ParseToken(token);
        _cached = new AuthenticationState(principal);

        if (principal.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(refreshToken))
        {
            CurrentSession = new AuthenticationResponse(
                Guid.Empty,
                principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
                principal.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty,
                principal.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty,
                principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty,
                token!,
                refreshToken);
        }

        return _cached;
    }

    public async Task SignInAsync(AuthenticationResponse response)
    {
        CurrentSession = response;

        await _js.InvokeVoidAsync("localStorage.setItem", TokenStorageKey, response.Token);
        await _js.InvokeVoidAsync("localStorage.setItem", RefreshTokenStorageKey, response.RefreshToken);

        NotifyStateChanged();
    }

    public async Task SignOutAsync()
    {
        CurrentSession = null;

        await _js.InvokeVoidAsync("localStorage.removeItem", TokenStorageKey);
        await _js.InvokeVoidAsync("localStorage.removeItem", RefreshTokenStorageKey);

        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        _cached = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal ParseToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        try
        {
            var payload = token.Split('.')[1];
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(Base64UrlDecode(payload)));

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("exp", out var expElement) &&
                expElement.TryGetInt64(out var exp) &&
                DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow)
            {
                return new ClaimsPrincipal(new ClaimsIdentity());
            }

            var email = GetString(root, "email");
            var role = GetString(root, "role");
            var givenName = GetString(root, "given_name");
            var surname = GetString(root, "family_name");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Email, email ?? string.Empty),
                new(ClaimTypes.Role, role ?? string.Empty),
                new(ClaimTypes.GivenName, givenName ?? string.Empty),
                new(ClaimTypes.Surname, surname ?? string.Empty)
            };

            if (!string.IsNullOrEmpty(givenName) || !string.IsNullOrEmpty(surname))
            {
                claims.Add(new Claim(ClaimTypes.Name, $"{givenName} {surname}".Trim()));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        }
        catch
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        return padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
