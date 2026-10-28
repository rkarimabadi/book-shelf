using BookStore.Contracts.Translation;
using Microsoft.AspNetCore.Components.Forms;

namespace BookStore.UI.Features.Admin.Services;

/// <summary>Payload of the "new translation" form, sent as multipart form data.</summary>
public sealed record TranslationJobSubmit(
    IBrowserFile BookFile,
    IBrowserFile? CoverFile,
    string? Title,
    string? Author,
    string Mode,
    string? Instructions);

/// <summary>Result of an admin translation-tool call (<see cref="AdminBookResult"/> plus an optional value).</summary>
public sealed record AdminTranslationResult<T>(
    bool Success,
    T? Value = default,
    string? Message = null,
    bool Unauthorized = false,
    bool Forbidden = false);

public interface IAdminTranslationService
{
    Task<AdminTranslationResult<IReadOnlyList<TranslationJobResponse>>> GetJobsAsync(CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<TranslationJobResponse>> CreateJobAsync(TranslationJobSubmit form, CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<bool>> CancelJobAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<bool>> RetryJobAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<bool>> DeleteJobAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Downloads the finished EPUB and hands it to the browser (<c>bookstoreDownload</c>).</summary>
    Task<AdminTranslationResult<bool>> DownloadAsync(TranslationJobResponse job, CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<AiSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<bool>> SaveSettingsAsync(UpdateAiSettingsRequest request, CancellationToken cancellationToken = default);

    Task<AdminTranslationResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default);
}
