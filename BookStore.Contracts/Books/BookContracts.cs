namespace BookStore.Contracts.Books;

public record CreateBookRequest(
    string Title,
    string Author,
    string Category,
    string Description);

public record UpdateBookRequest(
    string Title,
    string Author,
    string Category,
    string Description);

public record BookResponse(
    Guid Id,
    string Title,
    string Author,
    string Category,
    string Description,
    string CoverImagePath,
    string FilePath,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsActive,
    double? AverageRating = null,
    int RatingsCount = 0);

public record UpdateBookStatusRequest(
    bool IsActive);

public record PagedBooksResponse(
    IReadOnlyList<BookResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record BookNoteResponse(
    string Note);

public record SaveBookNoteRequest(
    string Note);

/// <summary>The requesting user's own rating for a book (null when they have not rated it) plus the public summary.</summary>
public record MyBookRatingResponse(
    double? Average,
    int Count,
    int? Rating);

public record SaveBookRatingRequest(
    int Rating);
