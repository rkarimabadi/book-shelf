namespace BookStore.Application.Common.Interfaces;

/// <summary>Encrypts secrets (the AI API key) before they are stored in the database.</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    /// <summary>The plain text, or null when the value can no longer be decrypted (e.g. lost keys).</summary>
    string? Unprotect(string protectedText);
}
