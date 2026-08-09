using System.Security.Cryptography;
using BookStore.Application.Common.Interfaces;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace BookStore.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 10000;
    private const char Delimiter = '.';

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: Iterations,
            numBytesRequested: KeySize);

        return $"{Convert.ToBase64String(salt)}{Delimiter}{Convert.ToBase64String(key)}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        var segments = passwordHash.Split(Delimiter);
        if (segments.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromBase64String(segments[0]);
        var expectedKey = Convert.FromBase64String(segments[1]);

        var actualKey = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: Iterations,
            numBytesRequested: KeySize);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
