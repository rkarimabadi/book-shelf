using BookStore.Application.Common.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BookStore.Infrastructure.Storage;

public sealed class CoverThumbnailGenerator : ICoverThumbnailGenerator
{
    private const string ThumbSubDirectory = "thumbs";

    private readonly IFileStorage _fileStorage;

    public CoverThumbnailGenerator(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    public async Task<string> GetOrCreateThumbnailAsync(string coverRelativePath, int width, CancellationToken cancellationToken = default)
    {
        var originalPath = _fileStorage.GetFullPath(coverRelativePath);
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("Cover file is missing.", originalPath);
        }

        var thumbPath = Path.Combine(ThumbDirectory(originalPath), $"{Path.GetFileName(originalPath)}.w{width}.webp");

        if (File.Exists(thumbPath))
        {
            return thumbPath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);

        using var image = await Image.LoadAsync(originalPath, cancellationToken);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(width, 0),
            Mode = ResizeMode.Max
        }));

        await image.SaveAsWebpAsync(thumbPath, new WebpEncoder { Quality = 82 }, cancellationToken);
        return thumbPath;
    }

    public void DeleteThumbnails(string coverRelativePath)
    {
        var originalPath = _fileStorage.GetFullPath(coverRelativePath);
        var directory = ThumbDirectory(originalPath);
        if (!Directory.Exists(directory))
        {
            return;
        }

        // "<cover file name>.w<width>.webp" — one file per width that was ever requested.
        foreach (var thumb in Directory.EnumerateFiles(directory, $"{Path.GetFileName(originalPath)}.w*.webp"))
        {
            File.Delete(thumb);
        }
    }

    private static string ThumbDirectory(string originalPath) =>
        Path.Combine(Path.GetDirectoryName(originalPath)!, ThumbSubDirectory);
}
