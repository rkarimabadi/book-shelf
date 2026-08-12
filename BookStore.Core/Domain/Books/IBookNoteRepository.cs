namespace BookStore.Core.Domain.Books;

public interface IBookNoteRepository
{
    Task<BookNote?> GetByUserAndBookAsync(Guid userId, Guid bookId, CancellationToken cancellationToken = default);

    void Add(BookNote note);

    void Update(BookNote note);

    void Delete(BookNote note);
}
