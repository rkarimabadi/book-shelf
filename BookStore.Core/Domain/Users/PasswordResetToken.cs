using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Users;

public class PasswordResetToken : Entity
{
    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsUsed { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;

    private PasswordResetToken() { }

    public static ErrorOr<PasswordResetToken> Create(string rawToken, DateTime expiresAt)
    {
        Guard.Against.NullOrEmpty(rawToken, nameof(rawToken));
        Guard.Against.ExpiresInPast(expiresAt, nameof(expiresAt));

        var resetToken = new PasswordResetToken
        {
            Token = HashToken(rawToken),
            ExpiresAt = expiresAt
        };

        return resetToken;
    }

    /// <summary>
    /// Reset tokens are stored hashed (SHA-256) so a database leak cannot be replayed
    /// against the reset endpoint. Only the raw token inside the emailed link can be used.
    /// </summary>
    public static string HashToken(string rawToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    public void MarkUsed()
    {
        if (IsUsed)
        {
            return;
        }

        IsUsed = true;
        UsedAt = DateTime.UtcNow;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiresAt;
    }
}
