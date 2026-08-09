namespace BookStore.Contracts.Library;

public record AddBookToLibraryRequest(
    Guid BookId);

public record LibraryBookResponse(
    Guid Id,
    string Title,
    string Author,
    string Description,
    string CoverImagePath,
    string FilePath,
    DateTime AddedAt);
