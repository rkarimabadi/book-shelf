using System.Net.Http.Json;
using BookStore.Contracts.Books;
using BookStore.Contracts.Library;
using BookStore.UI.Services;

namespace BookStore.UI.Features.Books.Services;

public sealed class BookService : IBookService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    // One 30 s cache entry per (page, pageSize, category) tuple, so browsing pages/filters doesn't re-fetch.
    private readonly Dictionary<string, (PagedBooksResponse Response, DateTimeOffset Time)> _cache = new();
    private IReadOnlyList<string>? _categories;
    private DateTimeOffset _categoriesTime;

    public BookService(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedBooksResponse?> GetBooksAsync(
        int page = 1,
        int pageSize = 12,
        string? category = null,
        string? search = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var key = $"{page}:{pageSize}:{category}:{search}";

        if (!forceRefresh && _cache.TryGetValue(key, out var cached) &&
            DateTimeOffset.UtcNow - cached.Time < CacheTtl)
        {
            return cached.Response;
        }

        try
        {
            var url = $"api/books?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(category))
            {
                url += $"&category={Uri.EscapeDataString(category)}";
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }

            var paged = await _http.GetFromJsonAsync<PagedBooksResponse>(url, cancellationToken);

            if (paged is not null)
            {
                _cache[key] = (paged, DateTimeOffset.UtcNow);
            }

            return paged;
        }
        catch
        {
            // Fall back to stale cache for this page if we have one; otherwise signal failure.
            return _cache.TryGetValue(key, out var stale) ? stale.Response : null;
        }
    }

    public async Task<IReadOnlyList<string>?> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (_categories is not null && DateTimeOffset.UtcNow - _categoriesTime < CacheTtl)
        {
            return _categories;
        }

        try
        {
            var categories = await _http.GetFromJsonAsync<List<string>>("api/books/categories", cancellationToken);
            _categories = categories ?? [];
            _categoriesTime = DateTimeOffset.UtcNow;
            return _categories;
        }
        catch
        {
            return _categories ?? null;
        }
    }

    public async Task<BookResponse?> GetBookAsync(Guid id, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // includeInactive is only used by the admin edit page; the server ignores it for non-admins.
            var url = includeInactive ? $"api/books/{id}?includeInactive=true" : $"api/books/{id}";
            return await _http.GetFromJsonAsync<BookResponse>(url, cancellationToken);
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

            var message = await ReadProblemMessageAsync(response, "افزودن به کتابخانه ناموفق بود؛ دوباره تلاش کنید.");
            return new AddToLibraryResult(false, message);
        }
        catch
        {
            return new AddToLibraryResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<RemoveFromLibraryResult> RemoveFromLibraryAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/library/{bookId}", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new RemoveFromLibraryResult(true, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new RemoveFromLibraryResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
            }

            var message = await ReadProblemMessageAsync(response, "حذف از کتابخانه ناموفق بود؛ دوباره تلاش کنید.");
            return new RemoveFromLibraryResult(false, message);
        }
        catch
        {
            return new RemoveFromLibraryResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<string?> GetBookNoteAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<BookNoteResponse>($"api/books/{bookId}/note", cancellationToken);
            return response?.Note;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SaveNoteResult> SaveBookNoteAsync(Guid bookId, string note, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"api/books/{bookId}/note", new SaveBookNoteRequest(note), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new SaveNoteResult(true, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new SaveNoteResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
            }

            var message = await ReadProblemMessageAsync(response, "ذخیرهٔ یادداشت ناموفق بود؛ دوباره تلاش کنید.");
            return new SaveNoteResult(false, message);
        }
        catch
        {
            return new SaveNoteResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    public async Task<MyBookRatingResponse?> GetBookRatingAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MyBookRatingResponse>($"api/books/{bookId}/rating", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SaveRatingResult> SaveBookRatingAsync(Guid bookId, int rating, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"api/books/{bookId}/rating", new SaveBookRatingRequest(rating), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new SaveRatingResult(true, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new SaveRatingResult(false, "نشست شما منقضی شده است؛ لطفاً دوباره وارد شوید.", Unauthorized: true);
            }

            var message = await ReadProblemMessageAsync(response, "ثبت امتیاز ناموفق بود؛ دوباره تلاش کنید.");
            return new SaveRatingResult(false, message);
        }
        catch
        {
            return new SaveRatingResult(false, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
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

            var message = await ReadProblemMessageAsync(response, "دانلود کتاب ناموفق بود؛ دوباره تلاش کنید.");
            return new DownloadResult(false, null, message);
        }
        catch
        {
            return new DownloadResult(false, null, "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.");
        }
    }

    private static async Task<string> ReadProblemMessageAsync(HttpResponseMessage response, string fallback)
    {
        var content = await response.Content.ReadAsStringAsync();
        var message = ProblemDetailsParser.ReadMessage(content);

        if (message is null)
        {
            return fallback;
        }

        if (message.Contains("already in the user's library", StringComparison.OrdinalIgnoreCase))
        {
            return "این کتاب قبلاً به کتابخانهٔ شما اضافه شده است.";
        }

        if (message.Contains("not in the user's library", StringComparison.OrdinalIgnoreCase))
        {
            return "این کتاب در کتابخانهٔ شما نیست.";
        }

        if (message == "Note must not exceed 1000 characters." || message == "BookNote.TooLong")
        {
            return "یادداشت نباید بیشتر از ۱۰۰۰ کاراکتر باشد.";
        }

        if (message == "Book.NotFound" || message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "کتاب پیدا نشد.";
        }

        if (message == "Book.Inactive" || message.Contains("is deactivated", StringComparison.OrdinalIgnoreCase))
        {
            return "این کتاب در حال حاضر غیرفعال است.";
        }

        if (message == "Book.FileMissing" || message.Contains("file is missing", StringComparison.OrdinalIgnoreCase))
        {
            return "فایل کتاب موجود نیست.";
        }

        if (message == "TranslationRating.InvalidRating" || message.Contains("must be between 1 and 5", StringComparison.OrdinalIgnoreCase))
        {
            return "امتیاز باید بین ۱ تا ۵ باشد.";
        }

        return message;
    }
}
