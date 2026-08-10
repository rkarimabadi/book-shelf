using BookStore.Core.Domain.Common;

namespace BookStore.Core.Domain.Users;

public abstract class UserDomainEvent : IDomainEvent
{
    public Guid UserId { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    protected UserDomainEvent(Guid userId)
    {
        UserId = userId;
    }
}

public class UserCreatedEvent : UserDomainEvent
{
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    public UserCreatedEvent(Guid userId, string email, string firstName, string lastName)
        : base(userId)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }
}

public class UserProfileUpdatedEvent : UserDomainEvent
{
    public string OldFirstName { get; }
    public string OldLastName { get; }
    public string NewFirstName { get; }
    public string NewLastName { get; }

    public UserProfileUpdatedEvent(Guid userId, string oldFirstName, string oldLastName, string newFirstName, string newLastName)
        : base(userId)
    {
        OldFirstName = oldFirstName;
        OldLastName = oldLastName;
        NewFirstName = newFirstName;
        NewLastName = newLastName;
    }
}

public class UserLoggedInEvent : UserDomainEvent
{
    public string Email { get; }

    public UserLoggedInEvent(Guid userId, string email)
        : base(userId)
    {
        Email = email;
    }
}

public class RefreshTokenAddedEvent : UserDomainEvent
{
    public string Token { get; }
    public DateTime ExpiresAt { get; }

    public RefreshTokenAddedEvent(Guid userId, string token, DateTime expiresAt)
        : base(userId)
    {
        Token = token;
        ExpiresAt = expiresAt;
    }
}

public class RefreshTokenRevokedEvent : UserDomainEvent
{
    public string Token { get; }

    public RefreshTokenRevokedEvent(Guid userId, string token)
        : base(userId)
    {
        Token = token;
    }
}

public class BookAddedToLibraryEvent : UserDomainEvent
{
    public Guid BookId { get; }

    public BookAddedToLibraryEvent(Guid userId, Guid bookId)
        : base(userId)
    {
        BookId = bookId;
    }
}

public class BookRemovedFromLibraryEvent : UserDomainEvent
{
    public Guid BookId { get; }

    public BookRemovedFromLibraryEvent(Guid userId, Guid bookId)
        : base(userId)
    {
        BookId = bookId;
    }
}

public class UserDeactivatedEvent : UserDomainEvent
{
    public string Email { get; }

    public UserDeactivatedEvent(Guid userId, string email)
        : base(userId)
    {
        Email = email;
    }
}

public class UserActivatedEvent : UserDomainEvent
{
    public string Email { get; }

    public UserActivatedEvent(Guid userId, string email)
        : base(userId)
    {
        Email = email;
    }
}

public class PasswordResetRequestedEvent : UserDomainEvent
{
    public string Email { get; }

    public PasswordResetRequestedEvent(Guid userId, string email)
        : base(userId)
    {
        Email = email;
    }
}

public class PasswordResetCompletedEvent : UserDomainEvent
{
    public string Email { get; }

    public PasswordResetCompletedEvent(Guid userId, string email)
        : base(userId)
    {
        Email = email;
    }
}