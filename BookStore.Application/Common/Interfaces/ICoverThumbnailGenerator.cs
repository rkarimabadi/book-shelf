namespace BookStore.Application.Common.Interfaces;

public interface ICoverThumbnailGenerator
{
    /// <summary>
    /// Returns the absolute path of a cached webp thumbnail (at most <paramref name="width"/> px wide)
    /// for the cover at <paramref name="coverRelativePath"/> (e.g. "uploads/covers/x.jpg"), generating
    /// and caching it on first request. Throws <see cref="FileNotFoundException"/> when the cover is missing.
    /// </summary>
    Task<string> GetOrCreateThumbnailAsync(string coverRelativePath, int width, CancellationToken cancellationToken = default);
}
