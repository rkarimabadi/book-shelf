using System.Security.Cryptography;
using System.Text;

namespace BookStore.Core.Domain.Users;

/// <summary>
/// Rules for the short numeric code mailed to confirm an address at registration. Only a hash is
/// stored on the user (salted with the user id); the plain code exists only in the email.
/// </summary>
public static class EmailVerificationCodes
{
    public const int Length = 6;

    /// <summary>Wrong guesses allowed before the code stops working (5 in a million).</summary>
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Minimum wait before a fresh code may be requested (shorter than <see cref="Lifetime"/>, so a valid code is always obtainable).</summary>
    public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, Length)).ToString($"D{Length}");

    public static string Hash(Guid userId, string plainCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:email-verification:{plainCode}")));

    public static bool Matches(Guid userId, string plainCode, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(userId, plainCode)),
            Encoding.ASCII.GetBytes(storedHash));

    /// <summary>
    /// Accepts what people actually type: Persian or Arabic digits, spaces and dashes.
    /// Returns null when the input cannot be a code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new StringBuilder(Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case >= '0' and <= '9':
                    digits.Append(ch);
                    break;
                case >= '۰' and <= '۹':
                    digits.Append((char)('0' + (ch - '۰')));
                    break;
                case >= '٠' and <= '٩':
                    digits.Append((char)('0' + (ch - '٠')));
                    break;
                case ' ' or '-' or '‌':
                    break;
                default:
                    return null;
            }
        }

        return digits.Length == Length ? digits.ToString() : null;
    }
}
