using BookStore.Core.Domain.Books;
using Microsoft.EntityFrameworkCore;

namespace BookStore.Infrastructure.Persistence.Repositories;

public sealed class BookNoteRepository : IBookNoteRepository
{
    private readonly BookStoreDbContext _dbContext;

    public BookNoteRepository(BookStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<BookNote?> GetByUserAndBookAsync(Guid userId, Guid bookId, CancellationToken cancellationToken = default)
    {
        return _dbContext.BookNotes
            .FirstOrDefaultAsync(note => note.UserId == userId && note.BookId == bookId, cancellationToken);
    }

    public void Add(BookNote note)
    {
        _dbContext.BookNotes.Add(note);
    }

    public void Update(BookNote note)
    {
        _dbContext.BookNotes.Update(note);
    }

    public void Delete(BookNote note)
    {
        _dbContext.BookNotes.Remove(note);
    }
}
