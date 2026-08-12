using ErrorOr;

namespace BookStore.Core.Domain.Books;

public static class BookNoteErrors
{
    public static Error TooLong =>
        Error.Validation("BookNote.TooLong", $"Note must not exceed {BookNote.MaxLength} characters.");
}
