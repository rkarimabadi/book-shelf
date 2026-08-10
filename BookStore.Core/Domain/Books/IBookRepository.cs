using BookStore.Core.Domain.Books;

namespace BookStore.Core.Domain.Books;

public interface IBookRepository
{
    Book? GetById(Guid id);
    Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Book>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<Book>> GetActiveAsync(CancellationToken cancellationToken = default);
    void Add(Book book);
    void Update(Book book);
    void Delete(Book book);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
