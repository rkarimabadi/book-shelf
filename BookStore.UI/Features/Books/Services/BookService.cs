using System.Net.Http.Json;
using BookStore.Contracts.Books;
using BookStore.Contracts.Library;
using BookStore.UI.Services;

namespace BookStore.UI.Features.Books.Services;

public sealed class BookService : IBookService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    private List<BookResponse>? _cache;
    private DateTimeOffset _cacheTime;

    public BookService(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<BookResponse>?> GetBooksAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cache is not null && DateTimeOffset.UtcNow - _cacheTime < CacheTtl)
        {
            return _cache;
        }

        try
        {
            var books = await _http.GetFromJsonAsync<List<BookResponse>>("api/books", cancellationToken);
            _cache = books ?? [];
            _cacheTime = DateTimeOffset.UtcNow;
            return _cache;
        }
        catch
        {
            // Fall back to stale cache if we have one; otherwise signal failure.
            return _cache ?? null;
        }
    }

    public async Task<BookResponse?> GetBookAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<BookResponse>($"api/books/{id}", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<LibraryBookResponse>?> GetMyLibraryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LibraryBookResponse>>("api/library", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsInMyLibraryAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var library = await GetMyLibraryAsync(cancellationToken);
        return library?.Any(entry => entry.Id == bookId) ?? false;
    }

    public async Task<AddToLibraryResult> AddToLibraryAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/library", new AddBookToLibraryRequest(bookId), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new AddToLibraryResult(true, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new AddToLibraryResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
            }

            var message = await ReadProblemMessageAsync(response);
            return new AddToLibraryResult(false, message);
        }
        catch
        {
            return new AddToLibraryResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<DownloadResult> DownloadBookAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync($"api/books/{bookId}/download", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return new DownloadResult(true, content, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new DownloadResult(false, null, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
            }

            var message = await ReadProblemMessageAsync(response);
            return new DownloadResult(false, null, message);
        }
        catch
        {
            return new DownloadResult(false, null, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    private static async Task<string> ReadProblemMessageAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var message = ProblemDetailsParser.ReadMessage(content);

        if (message is not null &&
            message.Contains("already in the user's library", StringComparison.OrdinalIgnoreCase))
        {
            return "این کتاب قبلاً به کتابخانهٔ شما اضافه شده است.";
        }

        return message ?? "افزودن به کتابخانه ناموفق بود؛ دوباره تلاش کنید.";
    }
}
