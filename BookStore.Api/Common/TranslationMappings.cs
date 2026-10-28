using BookStore.Contracts.Translation;
using BookStore.Core.Domain.Translation;

namespace BookStore.Api.Common;

/// <summary>Domain enums ↔ the lowercase wire values in <see cref="TranslationValues"/>.</summary>
public static class TranslationMappings
{
    public static string ToWire(this AiProviderType provider) => provider switch
    {
        AiProviderType.Anthropic => TranslationValues.ProviderAnthropic,
        _ => TranslationValues.ProviderOpenAi
    };

    public static AiProviderType? ParseProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        TranslationValues.ProviderOpenAi => AiProviderType.OpenAiCompatible,
        TranslationValues.ProviderAnthropic => AiProviderType.Anthropic,
        _ => null
    };

    public static string ToWire(this TranslationMode mode) => mode switch
    {
        TranslationMode.Translate => TranslationValues.ModeTranslate,
        TranslationMode.Edit => TranslationValues.ModeEdit,
        _ => TranslationValues.ModeAuto
    };

    public static TranslationMode? ParseMode(string? value) => (value ?? TranslationValues.ModeAuto).Trim().ToLowerInvariant() switch
    {
        "" or TranslationValues.ModeAuto => TranslationMode.Auto,
        TranslationValues.ModeTranslate => TranslationMode.Translate,
        TranslationValues.ModeEdit => TranslationMode.Edit,
        _ => null
    };

    public static string ToWire(this TranslationJobStatus status) => status switch
    {
        TranslationJobStatus.Running => TranslationValues.StatusRunning,
        TranslationJobStatus.Completed => TranslationValues.StatusCompleted,
        TranslationJobStatus.Failed => TranslationValues.StatusFailed,
        TranslationJobStatus.Cancelled => TranslationValues.StatusCancelled,
        _ => TranslationValues.StatusQueued
    };

    public static TranslationJobResponse ToResponse(this TranslationJob job) => new(
        job.Id,
        job.OriginalFileName,
        job.SourceTitle,
        job.Title,
        job.Author,
        job.Mode.ToWire(),
        job.Instructions,
        job.HasCustomCover,
        job.SourceWords,
        job.Status.ToWire(),
        job.TotalChunks,
        job.CompletedChunks,
        job.FallbackChunks,
        job.ModelName,
        job.ErrorMessage,
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt);
}
