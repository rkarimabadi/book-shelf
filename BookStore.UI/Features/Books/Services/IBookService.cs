using BookStore.Contracts.Books;
using BookStore.Contracts.Library;

namespace BookStore.UI.Features.Books.Services;

public sealed record AddToLibraryResult(bool Success, string? Message, bool Unauthorized = false);

public sealed record RemoveFromLibraryResult(bool Success, string? Message, bool Unauthorized = false);

public sealed record DownloadResult(bool Success, byte[]? Content, string? Message, bool Unauthorized = false);

public sealed record SaveNoteResult(bool Success, string? Message, bool Unauthorized = false);

public sealed record SaveRatingResult(bool Success, string? Message, bool Unauthorized = false);

public interface IBookService
{
    /// <summary>Returns one page of books (optionally filtered by category and/or title search) plus paging metadata; null when the API call failed.</summary>
    Task<PagedBooksResponse?> GetBooksAsync(int page = 1, int pageSize = 12, string? category = null, string? search = null, bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Returns the fixed category catalog (Persian labels); null when the API call failed.</summary>
    Task<IReadOnlyList<string>?> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a single book; null when not found or the API call failed.</summary>
    Task<BookResponse?> GetBookAsync(Guid id, bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>Returns the current user's library; null when the API call failed.</summary>
    Task<IReadOnlyList<LibraryBookResponse>?> GetMyLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the book is already in the current user's library.</summary>
    Task<bool> IsInMyLibraryAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Adds the book to the current user's library. Success = false + Message on failure.</summary>
    Task<AddToLibraryResult> AddToLibraryAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Removes the book from the current user's library. Success = false + Message on failure.</summary>
    Task<RemoveFromLibraryResult> RemoveFromLibraryAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Downloads the book file for the current user. Success = false + Message on failure.</summary>
    Task<DownloadResult> DownloadBookAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Returns the current user's private note for a book ("" when none); null on API failure.</summary>
    Task<string?> GetBookNoteAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Saves the current user's private note for a book (empty clears it). Success = false + Message on failure.</summary>
    Task<SaveNoteResult> SaveBookNoteAsync(Guid bookId, string note, CancellationToken cancellationToken = default);

    /// <summary>Returns the book's rating summary plus the current user's own rating; null on API failure.</summary>
    Task<MyBookRatingResponse?> GetBookRatingAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Saves/overwrites the current user's 1–5 star rating. Success = false + Message on failure.</summary>
    Task<SaveRatingResult> SaveBookRatingAsync(Guid bookId, int rating, CancellationToken cancellationToken = default);

    /// <summary>Builds a root-relative URL for an uploaded file path (e.g. "uploads/covers/x.jpg").</summary>
    static string MediaUrl(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : $"/{path.TrimStart('/')}";

    /// <summary>Builds the server-resized webp thumbnail URL for a cover path (used on list cards; the
    /// full-size image is only shown on the book details page).</summary>
    static string CoverThumbnailUrl(string? path, int width = 256) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : $"/api/books/covers/{Uri.EscapeDataString(Path.GetFileName(path.TrimEnd('/')))}?w={width}";
}
