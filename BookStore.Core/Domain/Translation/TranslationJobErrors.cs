using ErrorOr;

namespace BookStore.Core.Domain.Translation;

public static class TranslationJobErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("TranslationJob.NotFound", $"Translation job with id '{id}' was not found.");

    public static Error TitleTooLong =>
        Error.Validation("TranslationJob.TitleTooLong", $"Title must not exceed {TranslationJob.MaxTitleLength} characters.");

    public static Error AuthorTooLong =>
        Error.Validation("TranslationJob.AuthorTooLong", $"Author must not exceed {TranslationJob.MaxAuthorLength} characters.");

    public static Error InstructionsTooLong =>
        Error.Validation("TranslationJob.InstructionsTooLong", $"Instructions must not exceed {TranslationJob.MaxInstructionsLength} characters.");

    public static Error InvalidMode =>
        Error.Validation("TranslationJob.InvalidMode", "Translation mode is invalid.");

    public static Error NotActive =>
        Error.Conflict("TranslationJob.NotActive", "Translation job is not queued or running.");

    public static Error CannotCancel =>
        Error.Conflict("TranslationJob.CannotCancel", "Only a queued or running translation job can be cancelled.");

    public static Error CannotRetry =>
        Error.Conflict("TranslationJob.CannotRetry", "Only a failed or cancelled translation job can be retried.");

    public static Error StillActive =>
        Error.Conflict("TranslationJob.StillActive", "Cancel the translation job before deleting it.");

    public static Error NotCompleted =>
        Error.Conflict("TranslationJob.NotCompleted", "The translated book is not ready yet.");

    public static Error OutputMissing =>
        Error.NotFound("TranslationJob.OutputMissing", "The translated book file is missing.");

    public static Error InvalidEpub =>
        Error.Validation("TranslationJob.InvalidEpub", "The file is not a valid EPUB book.");

    public static Error DrmProtected =>
        Error.Validation("TranslationJob.DrmProtected", "The EPUB is DRM-protected and cannot be translated.");

    public static Error NoText =>
        Error.Validation("TranslationJob.NoText", "The EPUB contains no text to translate.");

    public static Error InvalidCover =>
        Error.Validation("TranslationJob.InvalidCover", "Cover image must be a JPG, PNG, WEBP or GIF file.");
}
