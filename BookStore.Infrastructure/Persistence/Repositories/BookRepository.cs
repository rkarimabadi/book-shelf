using BookStore.Core.Domain.Books;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

public sealed class BookRepository : IBookRepository
{
    private readonly BookStoreDbContext _dbContext;

    public BookRepository(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Book? GetById(Guid id)
    {
        return _dbContext.Books.FirstOrDefault(b => b.Id == id);
    }

    public Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Books.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public Task<List<Book>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Books
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Book>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Books
            .Where(b => b.IsActive)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public void Add(Book book)
    {
        _dbContext.Books.Add(book);
    }

    public void Update(Book book)
    {
        _dbContext.Books.Update(book);
    }

    public void Delete(Book book)
    {
        _dbContext.Books.Remove(book);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Books.AnyAsync(b => b.Id == id, cancellationToken);
    }
}
