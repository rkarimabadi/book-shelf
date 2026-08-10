using System.Text.Json;
using Microsoft.JSInterop;

namespace BookStore.UI.Services;

public sealed class ClientStorageService : IClientStorageService
{
    private readonly IJSRuntime _js;

    public ClientStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<T?> GetItemAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var raw = await _js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch (JsonException) when (typeof(T) == typeof(string))
        {
            // Auth tokens are stored as raw strings (not JSON-encoded) by AuthStateProvider,
            // so fall back to returning the raw value for string reads.
            return (T)(object)raw;
        }
    }

    public async Task SetItemAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var raw = JsonSerializer.Serialize(value);
        await _js.InvokeVoidAsync("localStorage.setItem", cancellationToken, key, raw);
    }

    public async Task RemoveItemAsync(string key, CancellationToken cancellationToken = default)
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, key);
    }
}
