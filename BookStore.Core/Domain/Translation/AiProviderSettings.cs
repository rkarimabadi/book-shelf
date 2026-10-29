using Ardalis.GuardClauses;
using BookStore.Core.Domain.Common;
using ErrorOr;

namespace BookStore.Core.Domain.Translation;

/// <summary>Wire protocol of the AI provider the translation tool talks to.</summary>
public enum AiProviderType
{
    /// <summary>OpenAI Chat Completions and every compatible server (OpenRouter, Groq, Ollama, …).</summary>
    OpenAiCompatible = 0,

    /// <summary>Anthropic Messages API.</summary>
    Anthropic = 1
}

/// <summary>
/// The admin's AI provider configuration (a single row). The API key is stored only in
/// protected (encrypted) form; protecting it is the infrastructure's job.
/// </summary>
public class AiProviderSettings : AggregateRoot
{
    public const int MaxEndpointLength = 500;
    public const int MaxModelLength = 200;
    public const int MinParallelRequests = 1;
    public const int MaxParallelRequests = 8;
    public const int MinChunkWords = 200;
    public const int MaxChunkWords = 2000;
    public const int DefaultParallelRequests = 3;
    public const int DefaultChunkWords = 700;

    public AiProviderType Provider { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string? ProtectedApiKey { get; private set; }
    public int ParallelRequests { get; private set; } = DefaultParallelRequests;
    public int ChunkWords { get; private set; } = DefaultChunkWords;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private AiProviderSettings() { }

    public static ErrorOr<AiProviderSettings> Create(
        AiProviderType provider,
        string endpoint,
        string model,
        int parallelRequests,
        int chunkWords,
        string? protectedApiKey)
    {
        var settings = new AiProviderSettings();
        var result = settings.Update(provider, endpoint, model, parallelRequests, chunkWords, protectedApiKey);
        if (result.IsError)
        {
            return result.Errors;
        }

        return settings;
    }

    /// <param name="protectedApiKey">The new protected key, or null to keep the stored one.</param>
    public ErrorOr<Success> Update(
        AiProviderType provider,
        string endpoint,
        string model,
        int parallelRequests,
        int chunkWords,
        string? protectedApiKey)
    {
        Guard.Against.Null(endpoint, nameof(endpoint));
        Guard.Against.Null(model, nameof(model));

        endpoint = endpoint.Trim();
        model = model.Trim();

        if (!Enum.IsDefined(provider))
        {
            return AiProviderSettingsErrors.InvalidProvider;
        }

        if (endpoint.Length > MaxEndpointLength
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return AiProviderSettingsErrors.InvalidEndpoint;
        }

        if (model.Length == 0 || model.Length > MaxModelLength)
        {
            return AiProviderSettingsErrors.InvalidModel;
        }

        if (parallelRequests is < MinParallelRequests or > MaxParallelRequests)
        {
            return AiProviderSettingsErrors.InvalidParallelRequests;
        }

        if (chunkWords is < MinChunkWords or > MaxChunkWords)
        {
            return AiProviderSettingsErrors.InvalidChunkWords;
        }

        var keyChanged = !string.IsNullOrWhiteSpace(protectedApiKey);

        Provider = provider;
        Endpoint = endpoint;
        Model = model;
        ParallelRequests = parallelRequests;
        ChunkWords = chunkWords;
        if (keyChanged)
        {
            ProtectedApiKey = protectedApiKey;
        }

        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new AiProviderSettingsUpdatedEvent(provider.ToString(), model, keyChanged));

        return Result.Success;
    }

    /// <summary>Removes the stored key (local servers such as Ollama need none).</summary>
    public void ClearApiKey()
    {
        if (ProtectedApiKey is null)
        {
            return;
        }

        ProtectedApiKey = null;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new AiProviderSettingsUpdatedEvent(Provider.ToString(), Model, apiKeyChanged: true));
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ProtectedApiKey);
}
