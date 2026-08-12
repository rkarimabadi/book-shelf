using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Books;

public class Book : AggregateRoot
{
    public string Title { get; private set; } = string.Empty;
    public string Author { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string CoverImagePath { get; private set; } = string.Empty;
    public string FilePath { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private Book() { }

    public static ErrorOr<Book> Create(
        string title,
        string author,
        string category,
        string description,
        string coverImagePath,
        string filePath)
    {
        var errors = new List<Error>();

        Guard.Against.NullOrEmpty(title, nameof(title));
        Guard.Against.NullOrEmpty(author, nameof(author));
        Guard.Against.NullOrEmpty(category, nameof(category));
        Guard.Against.NullOrEmpty(filePath, nameof(filePath));

        if (title.Length > 200)
        {
            errors.Add(Error.Validation("Book.TitleTooLong", "Title must not exceed 200 characters."));
        }

        if (author.Length > 100)
        {
            errors.Add(Error.Validation("Book.AuthorTooLong", "Author must not exceed 100 characters."));
        }

        if (!BookCategories.IsValid(category))
        {
            errors.Add(BookErrors.Validation.InvalidCategory);
        }

        if (errors.Any())
        {
            return errors;
        }

        var book = new Book
        {
            Title = title,
            Author = author,
            Category = category,
            Description = description ?? string.Empty,
            CoverImagePath = coverImagePath ?? string.Empty,
            FilePath = filePath
        };

        book.AddDomainEvent(new BookCreatedEvent(book.Id, title, author));

        return book;
    }

    public ErrorOr<Success> UpdateDetails(
        string title,
        string author,
        string category,
        string description,
        string? coverImagePath,
        string? filePath)
    {
        Guard.Against.NullOrEmpty(title, nameof(title));
        Guard.Against.NullOrEmpty(author, nameof(author));
        Guard.Against.NullOrEmpty(category, nameof(category));

        if (!BookCategories.IsValid(category))
        {
            return BookErrors.Validation.InvalidCategory;
        }

        var oldTitle = Title;
        var oldAuthor = Author;

        Title = title;
        Author = author;
        Category = category;
        Description = description ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(coverImagePath))
        {
            CoverImagePath = coverImagePath;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            FilePath = filePath;
        }

        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new BookUpdatedEvent(Id, oldTitle, oldAuthor, title, author));

        return Result.Success;
    }

    public void Delete()
    {
        AddDomainEvent(new BookDeletedEvent(Id, Title));
    }

    /// <summary>
    /// Deactivates the book: it disappears from the public list/details/download and from
    /// every user's library until reactivated.
    /// </summary>
    public ErrorOr<Success> Deactivate()
    {
        if (!IsActive)
        {
            return Result.Success;
        }

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new BookDeactivatedEvent(Id, Title));

        return Result.Success;
    }

    public ErrorOr<Success> Activate()
    {
        if (IsActive)
        {
            return Result.Success;
        }

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new BookActivatedEvent(Id, Title));

        return Result.Success;
    }
}
