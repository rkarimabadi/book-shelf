using System.Net.Http.Json;
using BookStore.Contracts.Authentication;

namespace BookStore.UI.Services;

public interface IAuthenticationService
{
    Task RegisterAsync(RegisterRequest request);
    Task<AuthenticationResponse> LoginAsync(LoginRequest request);
    Task LogoutAsync(string refreshToken);
    Task ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(string email, string token, string newPassword);
    Task ChangePasswordAsync(string currentPassword, string newPassword);
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

    public async Task ForgotPasswordAsync(string email)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_authUrl}/forgot-password", new ForgotPasswordRequest(email));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }
    }

    public async Task ResetPasswordAsync(string email, string token, string newPassword)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_authUrl}/reset-password",
            new ResetPasswordRequest(email, token, newPassword));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_authUrl}/change-password",
            new ChangePasswordRequest(currentPassword, newPassword));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(await ReadErrorAsync(response));
        }
    }

    private static async Task<AuthenticationResponse> ReadResultAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        return result ?? throw new Exception("Invalid authentication response.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return ProblemDetailsParser.ReadMessage(content) ?? $"درخواست ناموفق بود ({response.StatusCode}).";
    }
}
