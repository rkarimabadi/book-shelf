using BookStore.Core.Domain.Books;

namespace BookStore.Core.Domain.Books;

public interface IBookRepository
{
    Book? GetById(Guid id);
    Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of books (newest-first) plus the total matching count.
    /// When <paramref name="includeInactive"/> is false only active books are counted and returned;
    /// a non-empty <paramref name="category"/> further filters to that single category, and a
    /// non-empty <paramref name="search"/> filters to titles containing the text (case-insensitive).
    /// </summary>
    Task<(List<Book> Items, int TotalCount)> GetPageAsync(
        int page,
        int pageSize,
        bool includeInactive = false,
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default);
    void Add(Book book);
    void Update(Book book);
    void Delete(Book book);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
