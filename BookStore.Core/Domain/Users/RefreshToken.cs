using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Users;

public class RefreshToken : Entity
{
    public string Token { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime RevokedAt { get; private set; }
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;

    private RefreshToken() { }

    public static ErrorOr<RefreshToken> Create(string token, DateTime expiresAt, Guid userId)
    {
        Guard.Against.NullOrEmpty(token, nameof(token));
        Guard.Against.ExpiresInPast(expiresAt, nameof(expiresAt));

        var refreshToken = new RefreshToken
        {
            Token = token,
            ExpiresAt = expiresAt
        };

        return refreshToken;
    }

    public void Revoke()
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiresAt;
    }

    public bool IsValid()
    {
        return !IsRevoked && !IsExpired();
    }
}