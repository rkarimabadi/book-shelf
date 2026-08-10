using ErrorOr;

namespace BookStore.Core.Domain.Books;

public static class BookErrors
{
    public static class Validation
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("Book.NotFound", $"Book with id '{id}' not found.");

        public static Error TitleTooLong =>
            Error.Validation("Book.TitleTooLong", "Title must not exceed 200 characters.");

        public static Error AuthorTooLong =>
            Error.Validation("Book.AuthorTooLong", "Author must not exceed 100 characters.");

        public static Error BookInactive =>
            Error.Validation("Book.Inactive", "Book is deactivated.");
    }
}
