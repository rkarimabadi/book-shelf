using BookStore.Core.Domain.Common;

namespace BookStore.Core.Domain.Translation;

public abstract class TranslationJobDomainEvent : IDomainEvent
{
    public Guid JobId { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    protected TranslationJobDomainEvent(Guid jobId)
    {
        JobId = jobId;
    }
}

public class TranslationJobCreatedEvent : TranslationJobDomainEvent
{
    public string FileName { get; }

    public TranslationJobCreatedEvent(Guid jobId, string fileName)
        : base(jobId)
    {
        FileName = fileName;
    }
}

public class TranslationJobCompletedEvent : TranslationJobDomainEvent
{
    public int FallbackChunks { get; }

    public TranslationJobCompletedEvent(Guid jobId, int fallbackChunks)
        : base(jobId)
    {
        FallbackChunks = fallbackChunks;
    }
}

public class TranslationJobFailedEvent : TranslationJobDomainEvent
{
    public string Reason { get; }

    public TranslationJobFailedEvent(Guid jobId, string reason)
        : base(jobId)
    {
        Reason = reason;
    }
}

public class TranslationJobCancelledEvent : TranslationJobDomainEvent
{
    public TranslationJobCancelledEvent(Guid jobId)
        : base(jobId)
    {
    }
}

/// <summary>Raised when the admin saves the AI provider settings (never carries the key).</summary>
public class AiProviderSettingsUpdatedEvent : IDomainEvent
{
    public string Provider { get; }
    public string Model { get; }
    public bool ApiKeyChanged { get; }
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public AiProviderSettingsUpdatedEvent(string provider, string model, bool apiKeyChanged)
    {
        Provider = provider;
        Model = model;
        ApiKeyChanged = apiKeyChanged;
    }
}
