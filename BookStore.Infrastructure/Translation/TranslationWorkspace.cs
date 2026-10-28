using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Translation;
using BookStore.Infrastructure.Translation.Epub;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.Infrastructure.Translation;

public sealed class TranslationOptions
{
    public const string SectionName = "Translation";

    /// <summary>Root of the per-job folders; must NOT be under wwwroot (anchored to the content root at startup).</summary>
    public string WorkRoot { get; set; } = "App_Data/translations";
}

/// <summary>Files of one job: <c>{WorkRoot}/{jobId}/</c> with source.epub, cover.*, chunks/, bible.json, output.epub.</summary>
public sealed class TranslationWorkspace : ITranslationWorkspace
{
    public const string SourceFileName = "source.epub";
    public const string OutputFileName = "output.epub";

    private readonly string _root;
    private readonly ILogger<TranslationWorkspace> _logger;

    public TranslationWorkspace(IOptions<TranslationOptions> options, ILogger<TranslationWorkspace> logger)
    {
        _root = Path.GetFullPath(options.Value.WorkRoot);
        _logger = logger;
    }

    public string JobFolder(Guid jobId) => Path.Combine(_root, jobId.ToString("N"));

    public string ChunkFolder(Guid jobId) => Path.Combine(JobFolder(jobId), "chunks");

    public string SourcePath(Guid jobId) => Path.Combine(JobFolder(jobId), SourceFileName);

    public string? CoverPath(Guid jobId)
    {
        var folder = JobFolder(jobId);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "cover.*").FirstOrDefault() : null;
    }

    public ErrorOr<EpubBookInfo> Inspect(Stream epub)
    {
        var loaded = EpubBook.Load(epub);
        if (loaded.IsError)
        {
            return loaded.Errors;
        }

        var book = loaded.Value;
        var words = book.Documents
            .Where(d => !d.IsToc)
            .Sum(d => EpubText.ToText(book.GetText(d.Href), d.Kind).Blocks.Sum(b => b.Words));
        if (words == 0)
        {
            return TranslationJobErrors.NoText;
        }

        return new EpubBookInfo(book.Title, book.Author, words);
    }

    public async Task SaveSourceAsync(Guid jobId, Stream epub, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(JobFolder(jobId));
        await using var file = File.Create(SourcePath(jobId));
        await epub.CopyToAsync(file, cancellationToken);
    }

    public async Task SaveCoverAsync(Guid jobId, Stream cover, string extension, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(JobFolder(jobId));
        await using var file = File.Create(Path.Combine(JobFolder(jobId), "cover" + extension.ToLowerInvariant()));
        await cover.CopyToAsync(file, cancellationToken);
    }

    public string? GetOutputPath(Guid jobId)
    {
        var path = Path.Combine(JobFolder(jobId), OutputFileName);
        return File.Exists(path) ? path : null;
    }

    public void Delete(Guid jobId)
    {
        try
        {
            var folder = JobFolder(jobId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the work folder of translation job {JobId}.", jobId);
        }
    }
}
