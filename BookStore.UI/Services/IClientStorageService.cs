namespace BookStore.UI.Services;

/// <summary>
/// Abstraction over browser localStorage so components never touch IJSRuntime directly.
/// </summary>
public interface IClientStorageService
{
    Task<T?> GetItemAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetItemAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task RemoveItemAsync(string key, CancellationToken cancellationToken = default);
}
