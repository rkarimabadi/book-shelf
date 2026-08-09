using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Users;

public class LibraryEntry : Entity
{
    public Guid BookId { get; private set; }
    public DateTime AddedAt { get; private init; } = DateTime.UtcNow;

    private LibraryEntry() { }

    public static ErrorOr<LibraryEntry> Create(Guid bookId)
    {
        Guard.Against.Default(bookId, nameof(bookId));

        return new LibraryEntry
        {
            BookId = bookId
        };
    }
}
