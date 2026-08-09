using BookStore.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Storage;

public sealed class LocalFileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string RootPath { get; set; } = "wwwroot/uploads";
    public string BaseUrl { get; set; } = "/uploads";
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly LocalFileStorageOptions _options;

    public LocalFileStorage(IOptions<LocalFileStorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> SaveAsync(Stream content, string fileName, string subDirectory, CancellationToken cancellationToken = default)
    {
        var sanitizedFileName = Path.GetFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{sanitizedFileName}";
        var relativePath = Path.Combine(_options.BaseUrl.Trim('/'), subDirectory, uniqueName).Replace('\\', '/');

        var fullDirectory = Path.Combine(_options.RootPath, subDirectory);
        Directory.CreateDirectory(fullDirectory);

        var fullPath = Path.Combine(fullDirectory, uniqueName);
        await using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        return relativePath;
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public string GetFullPath(string relativePath)
    {
        var trimmed = relativePath.TrimStart('/');
        return Path.Combine(_options.RootPath, trimmed.Replace('/', Path.DirectorySeparatorChar));
    }
}
