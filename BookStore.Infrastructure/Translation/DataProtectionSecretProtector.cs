using System.Security.Cryptography;
using BookStore.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace BookStore.Infrastructure.Translation;

/// <summary>
/// Encrypts the AI API key with ASP.NET Core DataProtection (the persisted <c>keys/</c> ring).
/// If the key ring is lost (ephemeral keys on a misconfigured host) the stored value can no
/// longer be read and the admin is asked to enter the key again.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("BookStore.AiSettings.ApiKey.v1");
    }

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string? Unprotect(string protectedText)
    {
        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
