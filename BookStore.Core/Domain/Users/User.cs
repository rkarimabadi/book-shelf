using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Users;

public class User : AggregateRoot
{
    public new Guid Id { get; private init; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Whether the account has a password the user actually knows. Password-registered
    /// accounts are true. Google-created accounts start false (the stored hash is a
    /// random never-known secret), so the change-password flow lets them <em>set</em> a
    /// password without proving a current one; once set (change/reset/admin-reset) it
    /// stays true and change-password behaves normally from then on.
    /// </summary>
    public bool HasPassword { get; private set; } = true;

    /// <summary>
    /// Whether the owner proved they read mail at <see cref="Email"/> (by registration code, by
    /// password reset, or through Google). Unconfirmed accounts cannot sign in. Defaults to true so
    /// accounts that predate email confirmation keep working.
    /// </summary>
    public bool EmailConfirmed { get; private set; } = true;

    // The mailed registration code: only its hash is stored. All null when no code is live.
    public string? EmailVerificationCodeHash { get; private set; }
    public DateTime? EmailVerificationCodeIssuedAt { get; private set; }
    public DateTime? EmailVerificationCodeExpiresAt { get; private set; }
    public int EmailVerificationFailedAttempts { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private readonly List<PasswordResetToken> _passwordResetTokens = new();
    public IReadOnlyCollection<PasswordResetToken> PasswordResetTokens => _passwordResetTokens.AsReadOnly();

    private readonly List<LibraryEntry> _libraryEntries = new();
    public IReadOnlyCollection<LibraryEntry> LibraryEntries => _libraryEntries.AsReadOnly();

    private User() { }

    public static ErrorOr<User> Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole role = UserRole.User,
        bool hasPassword = true,
        bool emailConfirmed = true)
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
            Role = role,
            HasPassword = hasPassword,
            EmailConfirmed = emailConfirmed
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

    public ErrorOr<Success> AddPasswordResetToken(string rawToken, DateTime expiresAt)
    {
        Guard.Against.NullOrEmpty(rawToken, nameof(rawToken));
        Guard.Against.ExpiresInPast(expiresAt, nameof(expiresAt));

        var resetToken = PasswordResetToken.Create(rawToken, expiresAt);
        _passwordResetTokens.Add(resetToken.Value);

        AddDomainEvent(new PasswordResetRequestedEvent(Id, Email));

        return Result.Success;
    }

    /// <summary>
    /// Invalidates every outstanding reset token for this user so only the most
    /// recent emailed link can ever be used (old links become invalid immediately).
    /// </summary>
    public void InvalidatePasswordResetTokens()
    {
        foreach (var token in _passwordResetTokens.Where(token => !token.IsUsed))
        {
            token.MarkUsed();
        }
    }

    public ErrorOr<Success> ResetPassword(string tokenHash, string newPasswordHash)
    {
        Guard.Against.NullOrEmpty(tokenHash, nameof(tokenHash));
        Guard.Against.NullOrEmpty(newPasswordHash, nameof(newPasswordHash));

        if (!IsActive)
        {
            return UserErrors.Validation.UserInactive(Email);
        }

        var resetToken = _passwordResetTokens.FirstOrDefault(token => token.Token == tokenHash);
        if (resetToken is null)
        {
            return UserErrors.Validation.ResetTokenNotFound;
        }

        if (resetToken.IsExpired())
        {
            return UserErrors.Validation.ResetTokenExpired;
        }

        if (resetToken.IsUsed)
        {
            return UserErrors.Validation.ResetTokenUsed;
        }

        PasswordHash = newPasswordHash;
        HasPassword = true;
        resetToken.MarkUsed();

        // The reset link only reached the owner's inbox, so it also proves the address.
        ClearEmailVerification();

        // A changed password invalidates every existing session; force a fresh login.
        foreach (var refreshToken in _refreshTokens.Where(rt => !rt.IsRevoked))
        {
            refreshToken.Revoke();
        }

        AddDomainEvent(new PasswordResetCompletedEvent(Id, Email));

        return Result.Success;
    }

    /// <summary>
    /// Starts (or restarts, once per <see cref="EmailVerificationCodes.ResendInterval"/>) email
    /// confirmation. Returns the plain code to mail; only its hash is kept.
    /// </summary>
    public ErrorOr<string> IssueEmailVerificationCode(DateTime nowUtc)
    {
        if (EmailConfirmed)
        {
            return UserErrors.Validation.EmailAlreadyConfirmed;
        }

        if (SecondsUntilNextVerificationCode(nowUtc) > 0)
        {
            return UserErrors.Validation.VerificationCodeResendTooSoon;
        }

        var plain = EmailVerificationCodes.Generate();
        EmailVerificationCodeHash = EmailVerificationCodes.Hash(Id, plain);
        EmailVerificationCodeIssuedAt = nowUtc;
        EmailVerificationCodeExpiresAt = nowUtc + EmailVerificationCodes.Lifetime;
        EmailVerificationFailedAttempts = 0;
        return plain;
    }

    /// <summary>Seconds the owner must still wait before a fresh code may be requested (0 = now).</summary>
    public int SecondsUntilNextVerificationCode(DateTime nowUtc)
    {
        if (EmailConfirmed || EmailVerificationCodeIssuedAt is not { } issuedAt)
        {
            return 0;
        }

        var wait = issuedAt + EmailVerificationCodes.ResendInterval - nowUtc;
        return wait > TimeSpan.Zero ? (int)Math.Ceiling(wait.TotalSeconds) : 0;
    }

    /// <summary>The mail could not be sent: drop the code so the owner may ask again right away.</summary>
    public void WithdrawEmailVerificationCode() => ClearVerificationCodeColumns();

    /// <summary>
    /// Checks a typed code. A wrong guess is counted (the caller must persist it either way); after
    /// <see cref="EmailVerificationCodes.MaxFailedAttempts"/> misses the code stops working.
    /// </summary>
    public ErrorOr<Success> ConfirmEmail(string? code, DateTime nowUtc)
    {
        if (EmailConfirmed)
        {
            return Result.Success;
        }

        if (EmailVerificationCodeHash is null
            || EmailVerificationCodeExpiresAt is not { } expiresAt
            || nowUtc >= expiresAt
            || EmailVerificationFailedAttempts >= EmailVerificationCodes.MaxFailedAttempts)
        {
            return UserErrors.Validation.VerificationCodeExpired;
        }

        var plain = EmailVerificationCodes.Normalize(code);
        if (plain is null || !EmailVerificationCodes.Matches(Id, plain, EmailVerificationCodeHash))
        {
            EmailVerificationFailedAttempts++;
            return UserErrors.Validation.InvalidVerificationCode;
        }

        ClearEmailVerification();
        return Result.Success;
    }

    /// <summary>
    /// Someone registers again with an address whose first registration was never confirmed. The
    /// earlier attempt may have been a squatter's (or a mistyped password), so the new details win.
    /// </summary>
    public ErrorOr<Success> RestartRegistration(string passwordHash, string firstName, string lastName)
    {
        if (EmailConfirmed)
        {
            return UserErrors.Validation.EmailAlreadyExists(Email);
        }

        Guard.Against.NullOrEmpty(passwordHash, nameof(passwordHash));
        Guard.Against.NullOrEmpty(firstName, nameof(firstName));
        Guard.Against.NullOrEmpty(lastName, nameof(lastName));

        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        HasPassword = true;
        return Result.Success;
    }

    /// <summary>
    /// An external provider (Google) vouched for the address. If it was still unconfirmed, whoever
    /// chose the password never proved they own the inbox (someone may have registered the address
    /// first to lie in wait), so that password is replaced by an unusable one and its sessions end.
    /// The owner can set a real one through password reset.
    /// </summary>
    public void ConfirmEmailThroughProvider(string unusablePasswordHash)
    {
        if (EmailConfirmed)
        {
            return;
        }

        Guard.Against.NullOrEmpty(unusablePasswordHash, nameof(unusablePasswordHash));

        ClearEmailVerification();
        PasswordHash = unusablePasswordHash;
        HasPassword = false;

        foreach (var refreshToken in _refreshTokens.Where(rt => !rt.IsRevoked))
        {
            refreshToken.Revoke();
        }
    }

    private void ClearEmailVerification()
    {
        EmailConfirmed = true;
        ClearVerificationCodeColumns();
    }

    private void ClearVerificationCodeColumns()
    {
        EmailVerificationCodeHash = null;
        EmailVerificationCodeIssuedAt = null;
        EmailVerificationCodeExpiresAt = null;
        EmailVerificationFailedAttempts = 0;
    }

    public ErrorOr<Success> ChangePassword(string newPasswordHash)
    {
        Guard.Against.NullOrEmpty(newPasswordHash, nameof(newPasswordHash));

        if (!IsActive)
        {
            return UserErrors.Validation.UserInactive(Email);
        }

        PasswordHash = newPasswordHash;
        HasPassword = true;

        // A changed password invalidates every existing session; force a fresh login.
        foreach (var refreshToken in _refreshTokens.Where(rt => !rt.IsRevoked))
        {
            refreshToken.Revoke();
        }

        AddDomainEvent(new PasswordChangedEvent(Id, Email));

        return Result.Success;
    }

    public ErrorOr<Success> ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            return UserErrors.Validation.InvalidUserRole(role);
        }

        if (Role == role)
        {
            return UserErrors.Validation.UserRoleAlreadySet(role);
        }

        var oldRole = Role;
        Role = role;

        AddDomainEvent(new UserRoleChangedEvent(Id, Email, oldRole, role));

        return Result.Success;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;

        // Blocking an account must kill its sessions too; a revoked refresh token forces re-login.
        foreach (var refreshToken in _refreshTokens.Where(rt => !rt.IsRevoked))
        {
            refreshToken.Revoke();
        }

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