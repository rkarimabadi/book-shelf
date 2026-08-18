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

        var thumbPath = Path.Combine(
            Path.GetDirectoryName(originalPath)!,
            ThumbSubDirectory,
            $"{Path.GetFileName(originalPath)}.w{width}.webp");

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
}
