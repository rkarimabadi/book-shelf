using System.Net.Http.Json;
using System.Text.Json;
using BookStore.Contracts.Authentication;

namespace BookStore.UI.Services;

public interface IAuthenticationService
{
    Task RegisterAsync(RegisterRequest request);
    Task<AuthenticationResponse> LoginAsync(LoginRequest request);
    Task LogoutAsync(string refreshToken);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly AuthStateProvider _authStateProvider;
    private readonly string _authUrl = "api/auth";

    public AuthenticationService(HttpClient httpClient, AuthStateProvider authStateProvider)
    {
        _httpClient = httpClient;
        _authStateProvider = authStateProvider;
    }

    public async Task RegisterAsync(RegisterRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/register", request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }

        var result = await ReadResultAsync(response);
        await _authStateProvider.SignInAsync(result);
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/login", request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }

        var result = await ReadResultAsync(response);
        await _authStateProvider.SignInAsync(result);
        return result;
    }

    public async Task LogoutAsync(string refreshToken)
    {
        try
        {
            await _httpClient.PostAsJsonAsync($"{_authUrl}/logout", new LogoutRequest(refreshToken));
        }
        catch
        {
            // Local sign-out must still proceed even if the server call fails.
        }
        finally
        {
            await _authStateProvider.SignOutAsync();
        }
    }

    private static async Task<AuthenticationResponse> ReadResultAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        return result ?? throw new Exception("Invalid authentication response.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("title", out var title) && !string.IsNullOrEmpty(title.GetString()))
            {
                return title.GetString()!;
            }

            if (root.TryGetProperty("detail", out var detail) && !string.IsNullOrEmpty(detail.GetString()))
            {
                return detail.GetString()!;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array &&
                        property.Value.GetArrayLength() > 0)
                    {
                        return property.Value[0].GetString() ?? "خطا در داده‌های ورودی.";
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"درخواست ناموفق بود ({response.StatusCode}).";
    }
}
