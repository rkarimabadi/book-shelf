namespace BookStore.Contracts.Translation;

/// <summary>Wire values shared by the API and the UI (provider, mode and status names).</summary>
public static class TranslationValues
{
    public const string ProviderOpenAi = "openai";
    public const string ProviderAnthropic = "anthropic";

    public const string ModeAuto = "auto";
    public const string ModeTranslate = "translate";
    public const string ModeEdit = "edit";

    public const string StatusQueued = "queued";
    public const string StatusRunning = "running";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string StatusCancelled = "cancelled";

    public const string DefaultOpenAiEndpoint = "https://api.openai.com/v1";
    public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";

    /// <summary>Upload caps (the server's request-body limit is ~30 MB).</summary>
    public const long EpubMaxBytes = 25 * 1024 * 1024;
    public const long CoverMaxBytes = 5 * 1024 * 1024;
}

public record TranslationJobResponse(
    Guid Id,
    string OriginalFileName,
    string SourceTitle,
    string? Title,
    string? Author,
    string Mode,
    string? Instructions,
    bool HasCustomCover,
    int SourceWords,
    string Status,
    int TotalChunks,
    int CompletedChunks,
    int FallbackChunks,
    string? ModelName,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public record CreateTranslationJobRequest(
    string? Title,
    string? Author,
    string? Mode,
    string? Instructions);

/// <summary>Saved AI settings; <c>Configured</c> is false (and the rest defaults) before the first save.</summary>
public record AiSettingsResponse(
    bool Configured,
    string Provider,
    string Endpoint,
    string Model,
    bool HasApiKey,
    string? ApiKeyHint,
    bool ApiKeyUnreadable,
    int ParallelRequests,
    int ChunkWords,
    DateTime? UpdatedAt);

/// <param name="ApiKey">A new key, or empty to keep the stored one.</param>
public record UpdateAiSettingsRequest(
    string Provider,
    string Endpoint,
    string Model,
    string? ApiKey,
    bool ClearApiKey,
    int ParallelRequests,
    int ChunkWords);

public record AiConnectionTestResponse(string Reply);
