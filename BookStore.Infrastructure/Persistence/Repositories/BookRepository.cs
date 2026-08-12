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

    public async Task<(List<Book> Items, int TotalCount)> GetPageAsync(
        int page,
        int pageSize,
        bool includeInactive = false,
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Books.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(b => b.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(b => b.Title.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
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
