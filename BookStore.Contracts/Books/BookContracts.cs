namespace BookStore.Contracts.Books;

public record CreateBookRequest(
    string Title,
    string Author,
    string Description);

public record UpdateBookRequest(
    string Title,
    string Author,
    string Description);

public record BookResponse(
    Guid Id,
    string Title,
    string Author,
    string Description,
    string CoverImagePath,
    string FilePath,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsActive);

public record UpdateBookStatusRequest(
    bool IsActive);
