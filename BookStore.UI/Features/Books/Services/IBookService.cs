using BookStore.Contracts.Books;
using BookStore.Contracts.Library;

namespace BookStore.UI.Features.Books.Services;

public sealed record AddToLibraryResult(bool Success, string? Message, bool Unauthorized = false);

public sealed record DownloadResult(bool Success, byte[]? Content, string? Message, bool Unauthorized = false);

public interface IBookService
{
    /// <summary>Returns all books; null when the API call failed.</summary>
    Task<IReadOnlyList<BookResponse>?> GetBooksAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Returns a single book; null when not found or the API call failed.</summary>
    Task<BookResponse?> GetBookAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the current user's library; null when the API call failed.</summary>
    Task<IReadOnlyList<LibraryBookResponse>?> GetMyLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the book is already in the current user's library.</summary>
    Task<bool> IsInMyLibraryAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Adds the book to the current user's library. Success = false + Message on failure.</summary>
    Task<AddToLibraryResult> AddToLibraryAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Downloads the book file for the current user. Success = false + Message on failure.</summary>
    Task<DownloadResult> DownloadBookAsync(Guid bookId, CancellationToken cancellationToken = default);

    /// <summary>Builds a root-relative URL for an uploaded file path (e.g. "uploads/covers/x.jpg").</summary>
    static string MediaUrl(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : $"/{path.TrimStart('/')}";
}
