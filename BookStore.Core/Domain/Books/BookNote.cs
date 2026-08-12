using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Books;

/// <summary>A private note a user writes about a book (one per user per book).</summary>
public class BookNote : Entity
{
    public const int MaxLength = 1000;

    public Guid UserId { get; private set; }
    public Guid BookId { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private BookNote() { }

    public static ErrorOr<BookNote> Create(Guid userId, Guid bookId, string note)
    {
        Guard.Against.Default(userId, nameof(userId));
        Guard.Against.Default(bookId, nameof(bookId));
        Guard.Against.Null(note, nameof(note));

        if (note.Length > MaxLength)
        {
            return BookNoteErrors.TooLong;
        }

        return new BookNote
        {
            UserId = userId,
            BookId = bookId,
            Note = note
        };
    }

    public ErrorOr<Success> Update(string note)
    {
        Guard.Against.Null(note, nameof(note));

        if (note.Length > MaxLength)
        {
            return BookNoteErrors.TooLong;
        }

        Note = note;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success;
    }
}
