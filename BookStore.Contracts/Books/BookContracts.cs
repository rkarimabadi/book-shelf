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
    bool IsActive);

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
