namespace BookStore.Contracts.Books;

/// <summary>The book file formats the catalog accepts (admin upload) and serves (download).</summary>
public static class BookFileFormats
{
    public const string Epub = ".epub";
    public const string Pdf = ".pdf";

    private static readonly string[] Allowed = [Epub, Pdf];

    /// <summary>Value for an HTML file input's <c>accept</c> attribute.</summary>
    public const string AcceptAttribute = ".epub,.pdf,application/epub+zip,application/pdf";

    public static bool IsAllowed(string? fileNameOrPath) =>
        Allowed.Contains(ExtensionOf(fileNameOrPath), StringComparer.Ordinal);

    /// <summary>Upper-case label for badges («EPUB» / «PDF»); empty for unknown formats.</summary>
    public static string LabelFor(string? fileNameOrPath) =>
        IsAllowed(fileNameOrPath) ? ExtensionOf(fileNameOrPath)[1..].ToUpperInvariant() : string.Empty;

    /// <summary>Content type for downloads; legacy/unknown files keep the original EPUB default.</summary>
    public static string ContentTypeFor(string? fileNameOrPath) =>
        ExtensionOf(fileNameOrPath) == Pdf ? "application/pdf" : "application/epub+zip";

    private static string ExtensionOf(string? fileNameOrPath) =>
        Path.GetExtension(fileNameOrPath ?? string.Empty).ToLowerInvariant();
}
