using ErrorOr;

namespace BookStore.Core.Domain.Translation;

public static class AiProviderSettingsErrors
{
    public static Error InvalidProvider =>
        Error.Validation("AiSettings.InvalidProvider", "AI provider is invalid.");

    public static Error InvalidEndpoint =>
        Error.Validation("AiSettings.InvalidEndpoint", "Endpoint must be an absolute http(s) URL.");

    public static Error InvalidModel =>
        Error.Validation("AiSettings.InvalidModel", $"Model is required and must not exceed {AiProviderSettings.MaxModelLength} characters.");

    public static Error InvalidParallelRequests =>
        Error.Validation("AiSettings.InvalidParallelRequests",
            $"Parallel requests must be between {AiProviderSettings.MinParallelRequests} and {AiProviderSettings.MaxParallelRequests}.");

    public static Error InvalidChunkWords =>
        Error.Validation("AiSettings.InvalidChunkWords",
            $"Chunk size must be between {AiProviderSettings.MinChunkWords} and {AiProviderSettings.MaxChunkWords} words.");

    public static Error NotConfigured =>
        Error.Conflict("AiSettings.NotConfigured", "AI provider settings are not configured.");

    public static Error ApiKeyMissing =>
        Error.Conflict("AiSettings.ApiKeyMissing", "The AI provider API key is missing; enter it again.");
}
