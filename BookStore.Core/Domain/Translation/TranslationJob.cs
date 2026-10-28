using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Translation;

/// <summary>
/// One run of the admin AI translation/editing tool: an uploaded EPUB (plus an optional
/// replacement cover) that a background worker turns into a Persian EPUB, chunk by chunk.
/// The chunk results live in the job's work folder, so a failed or interrupted job can be
/// queued again and continues where it stopped.
/// </summary>
public class TranslationJob : AggregateRoot
{
    public const int MaxInstructionsLength = 2000;
    public const int MaxTitleLength = 200;
    public const int MaxAuthorLength = 100;
    public const int MaxErrorLength = 1000;

    public Guid CreatedByUserId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string SourceTitle { get; private set; } = string.Empty;
    public string? Title { get; private set; }
    public string? Author { get; private set; }
    public TranslationMode Mode { get; private set; }
    public string? Instructions { get; private set; }
    public bool HasCustomCover { get; private set; }
    public int SourceWords { get; private set; }
    public TranslationJobStatus Status { get; private set; }
    public int TotalChunks { get; private set; }
    public int CompletedChunks { get; private set; }
    public int FallbackChunks { get; private set; }
    public string? ModelName { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private TranslationJob() { }

    public static ErrorOr<TranslationJob> Create(
        Guid createdByUserId,
        string originalFileName,
        string sourceTitle,
        int sourceWords,
        string? title,
        string? author,
        TranslationMode mode,
        string? instructions,
        bool hasCustomCover)
    {
        Guard.Against.Default(createdByUserId, nameof(createdByUserId));
        Guard.Against.NullOrWhiteSpace(originalFileName, nameof(originalFileName));
        Guard.Against.Negative(sourceWords, nameof(sourceWords));

        title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
        instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();

        if (title?.Length > MaxTitleLength)
        {
            return TranslationJobErrors.TitleTooLong;
        }

        if (author?.Length > MaxAuthorLength)
        {
            return TranslationJobErrors.AuthorTooLong;
        }

        if (instructions?.Length > MaxInstructionsLength)
        {
            return TranslationJobErrors.InstructionsTooLong;
        }

        if (!Enum.IsDefined(mode))
        {
            return TranslationJobErrors.InvalidMode;
        }

        var job = new TranslationJob
        {
            CreatedByUserId = createdByUserId,
            OriginalFileName = originalFileName.Trim(),
            SourceTitle = (sourceTitle ?? string.Empty).Trim(),
            SourceWords = sourceWords,
            Title = title,
            Author = author,
            Mode = mode,
            Instructions = instructions,
            HasCustomCover = hasCustomCover,
            Status = TranslationJobStatus.Queued
        };

        job.AddDomainEvent(new TranslationJobCreatedEvent(job.Id, job.OriginalFileName));

        return job;
    }

    public bool IsActive => Status is TranslationJobStatus.Queued or TranslationJobStatus.Running;

    /// <summary>Marks the job running. A resumed job (already Running) keeps its progress.</summary>
    public ErrorOr<Success> Start(int totalChunks, int completedChunks, string modelName)
    {
        Guard.Against.NegativeOrZero(totalChunks, nameof(totalChunks));
        Guard.Against.OutOfRange(completedChunks, nameof(completedChunks), 0, totalChunks);

        if (!IsActive)
        {
            return TranslationJobErrors.NotActive;
        }

        Status = TranslationJobStatus.Running;
        TotalChunks = totalChunks;
        CompletedChunks = completedChunks;
        ModelName = modelName;
        ErrorMessage = null;
        StartedAt ??= DateTime.UtcNow;

        return Result.Success;
    }

    public ErrorOr<Success> ReportProgress(int completedChunks, int fallbackChunks)
    {
        if (Status != TranslationJobStatus.Running)
        {
            return TranslationJobErrors.NotActive;
        }

        CompletedChunks = Math.Clamp(completedChunks, 0, TotalChunks);
        FallbackChunks = Math.Max(0, fallbackChunks);

        return Result.Success;
    }

    public ErrorOr<Success> Complete()
    {
        if (Status != TranslationJobStatus.Running)
        {
            return TranslationJobErrors.NotActive;
        }

        Status = TranslationJobStatus.Completed;
        CompletedChunks = TotalChunks;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = null;

        AddDomainEvent(new TranslationJobCompletedEvent(Id, FallbackChunks));

        return Result.Success;
    }

    public ErrorOr<Success> Fail(string message)
    {
        Guard.Against.NullOrWhiteSpace(message, nameof(message));

        if (!IsActive)
        {
            return TranslationJobErrors.NotActive;
        }

        Status = TranslationJobStatus.Failed;
        ErrorMessage = message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;
        CompletedAt = DateTime.UtcNow;

        AddDomainEvent(new TranslationJobFailedEvent(Id, ErrorMessage));

        return Result.Success;
    }

    public ErrorOr<Success> Cancel()
    {
        if (!IsActive)
        {
            return TranslationJobErrors.CannotCancel;
        }

        Status = TranslationJobStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;

        AddDomainEvent(new TranslationJobCancelledEvent(Id));

        return Result.Success;
    }

    /// <summary>Queues a failed or cancelled job again; finished chunks are reused.</summary>
    public ErrorOr<Success> Requeue()
    {
        if (Status is not (TranslationJobStatus.Failed or TranslationJobStatus.Cancelled))
        {
            return TranslationJobErrors.CannotRetry;
        }

        Status = TranslationJobStatus.Queued;
        ErrorMessage = null;
        CompletedAt = null;

        return Result.Success;
    }
}
