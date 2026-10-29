using ErrorOr;

namespace BookStore.Application.Common.Interfaces;

/// <summary>Basic facts read from an uploaded EPUB before a translation job is created.</summary>
public sealed record EpubBookInfo(string Title, string Author, int Words);

/// <summary>
/// Private per-job folder (outside <c>wwwroot</c>) holding the uploaded EPUB, the optional
/// replacement cover, the chunk results and the finished Persian EPUB.
/// </summary>
public interface ITranslationWorkspace
{
    /// <summary>Validates the EPUB (zip, OPF, no DRM, some text) and reads its title/author/word count.</summary>
    ErrorOr<EpubBookInfo> Inspect(Stream epub);

    Task SaveSourceAsync(Guid jobId, Stream epub, CancellationToken cancellationToken = default);

    Task SaveCoverAsync(Guid jobId, Stream cover, string extension, CancellationToken cancellationToken = default);

    /// <summary>Full path of the finished EPUB, or null when it does not exist.</summary>
    string? GetOutputPath(Guid jobId);

    /// <summary>Removes the whole job folder (best effort).</summary>
    void Delete(Guid jobId);
}

/// <summary>Hands jobs to the background translation worker.</summary>
public interface ITranslationQueue
{
    void Enqueue(Guid jobId);

    /// <summary>Stops the job if the worker is running it right now.</summary>
    void Cancel(Guid jobId);
}
