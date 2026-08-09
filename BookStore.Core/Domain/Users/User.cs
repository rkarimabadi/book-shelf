using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Users;

public class User : AggregateRoot
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private readonly List<LibraryEntry> _libraryEntries = new();
    public IReadOnlyCollection<LibraryEntry> LibraryEntries => _libraryEntries.AsReadOnly();

    private User() { }

    public static ErrorOr<User> Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole role = UserRole.User)
    {
        var errors = new List<Error>();

        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(passwordHash, nameof(passwordHash));
        Guard.Against.NullOrEmpty(firstName, nameof(firstName));
        Guard.Against.NullOrEmpty(lastName, nameof(lastName));

        if (!IsValidEmail(email))
        {
            errors.Add(Error.Validation("InvalidEmail", "Email is not valid."));
        }

        if (passwordHash.Length < 8)
        {
            errors.Add(Error.Validation("PasswordTooShort", "Password must be at least 8 characters long."));
        }

        if (errors.Any())
        {
            return errors;
        }

        var user = new User
        {
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            Role = role
        };

        user.AddDomainEvent(new UserCreatedEvent(user.Id, user.Email, user.FirstName, user.LastName));

        return user;
    }

    public ErrorOr<Success> UpdateProfile(string firstName, string lastName)
    {
        Guard.Against.NullOrEmpty(firstName, nameof(firstName));
        Guard.Against.NullOrEmpty(lastName, nameof(lastName));

        var oldFirstName = FirstName;
        var oldLastName = LastName;

        FirstName = firstName;
        LastName = lastName;

        AddDomainEvent(new UserProfileUpdatedEvent(Id, oldFirstName, oldLastName, firstName, lastName));

        return Result.Success;
    }

    public ErrorOr<Success> Login()
    {
        if (!IsActive)
        {
            return Error.Unauthorized("UserInactive", "User account is inactive.");
        }

        LastLoginAt = DateTime.UtcNow;
        AddDomainEvent(new UserLoggedInEvent(Id, Email));

        return Result.Success;
    }

    public ErrorOr<Success> AddRefreshToken(string token, DateTime expiresAt)
    {
        Guard.Against.NullOrEmpty(token, nameof(token));
        Guard.Against.ExpiresInPast(expiresAt, nameof(expiresAt));

        var refreshToken = RefreshToken.Create(token, expiresAt, Id);
        _refreshTokens.Add(refreshToken.Value);

        AddDomainEvent(new RefreshTokenAddedEvent(Id, token, expiresAt));

        return Result.Success;
    }

    public ErrorOr<Success> AddToLibrary(Guid bookId)
    {
        Guard.Against.Default(bookId, nameof(bookId));

        if (_libraryEntries.Any(entry => entry.BookId == bookId))
        {
            return UserErrors.Validation.BookAlreadyInLibrary(bookId);
        }

        var libraryEntry = LibraryEntry.Create(bookId);
        _libraryEntries.Add(libraryEntry.Value);

        AddDomainEvent(new BookAddedToLibraryEvent(Id, bookId));

        return Result.Success;
    }

    public ErrorOr<Success> RemoveFromLibrary(Guid bookId)
    {
        Guard.Against.Default(bookId, nameof(bookId));

        var libraryEntry = _libraryEntries.FirstOrDefault(entry => entry.BookId == bookId);
        if (libraryEntry is null)
        {
            return UserErrors.Validation.BookNotInLibrary(bookId);
        }

        _libraryEntries.Remove(libraryEntry);

        AddDomainEvent(new BookRemovedFromLibraryEvent(Id, bookId));

        return Result.Success;
    }

    public ErrorOr<Success> RevokeRefreshToken(string token)
    {
        Guard.Against.NullOrEmpty(token, nameof(token));

        var refreshToken = _refreshTokens.FirstOrDefault(rt => rt.Token == token && !rt.IsRevoked);
        if (refreshToken == null)
        {
            return Error.NotFound("RefreshTokenNotFound", "Refresh token not found or already revoked.");
        }

        refreshToken.Revoke();
        AddDomainEvent(new RefreshTokenRevokedEvent(Id, token));

        return Result.Success;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        AddDomainEvent(new UserDeactivatedEvent(Id, Email));
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        AddDomainEvent(new UserActivatedEvent(Id, Email));
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}