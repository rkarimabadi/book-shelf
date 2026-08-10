using BookStore.Core.Domain.Common;

namespace BookStore.Core.Domain.Books;

public abstract class BookDomainEvent : IDomainEvent
{
    public Guid BookId { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    protected BookDomainEvent(Guid bookId)
    {
        BookId = bookId;
    }
}

public class BookCreatedEvent : BookDomainEvent
{
    public string Title { get; }
    public string Author { get; }

    public BookCreatedEvent(Guid bookId, string title, string author)
        : base(bookId)
    {
        Title = title;
        Author = author;
    }
}

public class BookUpdatedEvent : BookDomainEvent
{
    public string OldTitle { get; }
    public string OldAuthor { get; }
    public string NewTitle { get; }
    public string NewAuthor { get; }

    public BookUpdatedEvent(Guid bookId, string oldTitle, string oldAuthor, string newTitle, string newAuthor)
        : base(bookId)
    {
        OldTitle = oldTitle;
        OldAuthor = oldAuthor;
        NewTitle = newTitle;
        NewAuthor = newAuthor;
    }
}

public class BookDeletedEvent : BookDomainEvent
{
    public string Title { get; }

    public BookDeletedEvent(Guid bookId, string title)
        : base(bookId)
    {
        Title = title;
    }
}

public class BookDeactivatedEvent : BookDomainEvent
{
    public string Title { get; }

    public BookDeactivatedEvent(Guid bookId, string title)
        : base(bookId)
    {
        Title = title;
    }
}

public class BookActivatedEvent : BookDomainEvent
{
    public string Title { get; }

    public BookActivatedEvent(Guid bookId, string title)
        : base(bookId)
    {
        Title = title;
    }
}
